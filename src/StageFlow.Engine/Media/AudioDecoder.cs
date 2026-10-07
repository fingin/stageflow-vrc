using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace StageFlow.Engine.Media;

/// <summary>Single-owner native audio decode/resample to 48 kHz interleaved stereo.</summary>
public sealed unsafe class AudioDecoder : IDisposable
{
    private AVFormatContext* _format;
    private AVCodecContext* _codec;
    private SwrContext* _resampler;
    private AVPacket* _packet;
    private AVFrame* _frame;
    private float* _samples;
    private const int Capacity = 65536;
    private int _stream, _sampleCount;
    private AVRational _timeBase;
    private long _startTime, _nextFrame;
    private bool _draining, _resamplerDrained;
    public long StartSample { get; private set; }
    public ReadOnlySpan<float> Samples => new(_samples, _sampleCount * 2);

    public AudioDecoder(string path)
    {
        try
        {
            AVFormatContext* format = null;
            FFmpegRuntime.Check(ffmpeg.avformat_open_input(&format, path, null, null), "Open audio"); _format = format;
            FFmpegRuntime.Check(ffmpeg.avformat_find_stream_info(_format, null), "Inspect audio");
            AVCodec* decoder = null;
            _stream = FFmpegRuntime.Check(ffmpeg.av_find_best_stream(_format, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, &decoder, 0), "Find audio stream");
            var stream = _format->streams[_stream]; _timeBase = stream->time_base;
            _startTime = stream->start_time == ffmpeg.AV_NOPTS_VALUE ? 0 : stream->start_time;
            _codec = ffmpeg.avcodec_alloc_context3(decoder);
            if (_codec == null) throw new OutOfMemoryException();
            FFmpegRuntime.Check(ffmpeg.avcodec_parameters_to_context(_codec, stream->codecpar), "Configure audio decoder");
            FFmpegRuntime.Check(ffmpeg.avcodec_open2(_codec, decoder, null), "Open audio decoder");
            AVChannelLayout stereo = default; ffmpeg.av_channel_layout_default(&stereo, 2);
            SwrContext* resampler = null;
            try { FFmpegRuntime.Check(ffmpeg.swr_alloc_set_opts2(&resampler, &stereo, AVSampleFormat.AV_SAMPLE_FMT_FLT, 48000,
                &_codec->ch_layout, _codec->sample_fmt, _codec->sample_rate, 0, null), "Configure audio resampler"); }
            finally { ffmpeg.av_channel_layout_uninit(&stereo); _resampler = resampler; }
            FFmpegRuntime.Check(ffmpeg.swr_init(_resampler), "Initialize audio resampler");
            _packet = ffmpeg.av_packet_alloc(); _frame = ffmpeg.av_frame_alloc();
            _samples = (float*)NativeMemory.Alloc((nuint)(Capacity * 2 * sizeof(float)));
            if (_packet == null || _frame == null || _samples == null) throw new OutOfMemoryException();
        }
        catch { Dispose(); throw; }
    }
    public bool Read()
    {
        ffmpeg.av_frame_unref(_frame);
        while (true)
        {
            int result = ffmpeg.avcodec_receive_frame(_codec, _frame);
            if (result == 0)
            {
                long delay = ffmpeg.swr_get_delay(_resampler, _codec->sample_rate);
                StartSample = _frame->best_effort_timestamp == ffmpeg.AV_NOPTS_VALUE ? _nextFrame :
                    ffmpeg.av_rescale_q(_frame->best_effort_timestamp - _startTime, _timeBase, new AVRational { num = 1, den = 48000 }) -
                    delay * 48000 / _codec->sample_rate;
                int required = ffmpeg.swr_get_out_samples(_resampler, _frame->nb_samples);
                if (required > Capacity) throw new InvalidDataException("Audio frame exceeds bounded decode capacity.");
                byte* output = (byte*)_samples;
                _sampleCount = FFmpegRuntime.Check(ffmpeg.swr_convert(_resampler, &output, Capacity, _frame->extended_data, _frame->nb_samples), "Resample audio");
                _nextFrame = StartSample + _sampleCount;
                if (_sampleCount > 0) return true;
                ffmpeg.av_frame_unref(_frame); continue;
            }
            if (result == ffmpeg.AVERROR_EOF)
            {
                if (_resamplerDrained) { _sampleCount = 0; return false; }
                byte* output = (byte*)_samples;
                _sampleCount = FFmpegRuntime.Check(ffmpeg.swr_convert(_resampler, &output, Capacity, null, 0), "Drain audio resampler");
                StartSample = _nextFrame; _nextFrame += _sampleCount; _resamplerDrained = _sampleCount == 0;
                return _sampleCount > 0;
            }
            if (result != ffmpeg.AVERROR(ffmpeg.EAGAIN)) FFmpegRuntime.Check(result, "Decode audio");
            if (_draining) { _sampleCount = 0; return false; }
            do { ffmpeg.av_packet_unref(_packet); result = ffmpeg.av_read_frame(_format, _packet); }
            while (result >= 0 && _packet->stream_index != _stream);
            if (result == ffmpeg.AVERROR_EOF)
            { FFmpegRuntime.Check(ffmpeg.avcodec_send_packet(_codec, null), "Drain audio decoder"); _draining = true; }
            else
            {
                FFmpegRuntime.Check(result, "Read audio packet");
                FFmpegRuntime.Check(ffmpeg.avcodec_send_packet(_codec, _packet), "Submit audio packet");
                ffmpeg.av_packet_unref(_packet);
            }
        }
    }
    public void ReadAt(long startSample, Span<float> stereo)
    {
        if ((stereo.Length & 1) != 0) throw new ArgumentException("Stereo destination must contain pairs.");
        stereo.Clear();
        if (startSample < 0)
        {
            int silence = (int)Math.Min(-startSample, stereo.Length / 2);
            stereo = stereo[(silence * 2)..]; startSample += silence;
            if (stereo.IsEmpty) return;
        }
        if (startSample < StartSample || startSample > _nextFrame + 48000) Seek(startSample);
        int written = 0;
        while (written < stereo.Length / 2)
        {
            long target = startSample + written;
            if (_sampleCount == 0 || target >= StartSample + _sampleCount)
            { if (!Read()) break; continue; }
            if (target < StartSample)
            { written += (int)Math.Min(StartSample - target, stereo.Length / 2 - written); continue; }
            int source = checked((int)(target - StartSample));
            int frames = Math.Min(_sampleCount - source, stereo.Length / 2 - written);
            Samples.Slice(source * 2, frames * 2).CopyTo(stereo.Slice(written * 2, frames * 2)); written += frames;
        }
    }
    public void Seek(long sample)
    {
        long time = ffmpeg.av_rescale_q(sample, new AVRational { num = 1, den = 48000 }, _timeBase) + _startTime;
        FFmpegRuntime.Check(ffmpeg.av_seek_frame(_format, _stream, time, ffmpeg.AVSEEK_FLAG_BACKWARD), "Seek audio");
        ffmpeg.avcodec_flush_buffers(_codec); ffmpeg.av_frame_unref(_frame); ffmpeg.av_packet_unref(_packet);
        ffmpeg.swr_close(_resampler); FFmpegRuntime.Check(ffmpeg.swr_init(_resampler), "Reset audio resampler");
        _draining = _resamplerDrained = false; _sampleCount = 0; _nextFrame = StartSample = sample;
    }
    public void Dispose()
    {
        var swr = _resampler; _resampler = null; if (swr != null) ffmpeg.swr_free(&swr);
        var frame = _frame; _frame = null; if (frame != null) ffmpeg.av_frame_free(&frame);
        var packet = _packet; _packet = null; if (packet != null) ffmpeg.av_packet_free(&packet);
        var codec = _codec; _codec = null; if (codec != null) ffmpeg.avcodec_free_context(&codec);
        var format = _format; _format = null; if (format != null) ffmpeg.avformat_close_input(&format);
        if (_samples != null) { NativeMemory.Free(_samples); _samples = null; }
    }
}
