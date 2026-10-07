using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using StageFlow.Engine.Media;

namespace StageFlow.Engine.Streaming;

/// <summary>In-memory MPEG-TS segments. Both streams retain continuous live timestamps across show seeks.</summary>
public sealed unsafe class TsSegmentMuxer : IDisposable
{
    private readonly HlsSegmentStore _store;
    private readonly AVCodecContext* _video;
    private readonly AVCodecContext* _audio;
    private AVFormatContext* _format;
    private AVPacket* _copy;
    private double _start = double.NaN, _end;
    private readonly double _target;
    public TsSegmentMuxer(HlsSegmentStore store, AVCodecContext* video, AVCodecContext* audio, double targetSeconds)
    {
        _store = store; _video = video; _audio = audio; _target = targetSeconds;
        _copy = ffmpeg.av_packet_alloc();
        if (_copy == null) throw new OutOfMemoryException();
    }
    private static double Seconds(long pts, AVRational timeBase) => pts * (double)timeBase.num / timeBase.den;
    public void WriteVideo(AVPacket* packet)
    {
        double time = Seconds(packet->pts, _video->time_base);
        bool key = (packet->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0;
        if (_format != null && key && time - _start >= _target * 0.8) Finish(time);
        if (_format == null)
        {
            if (!key) return;
            Open(); _start = time;
        }
        _end = Math.Max(_end, time + Seconds(Math.Max(1, packet->duration), _video->time_base));
        Write(packet, 0, _video->time_base);
    }
    public void WriteAudio(AVPacket* packet)
    {
        if (_format == null) return;
        Write(packet, 1, _audio->time_base);
    }
    private void Write(AVPacket* packet, int stream, AVRational timeBase)
    {
        ffmpeg.av_packet_unref(_copy);
        FFmpegRuntime.Check(ffmpeg.av_packet_ref(_copy, packet), "Reference encoded packet");
        _copy->stream_index = stream;
        ffmpeg.av_packet_rescale_ts(_copy, timeBase, _format->streams[stream]->time_base);
        FFmpegRuntime.Check(ffmpeg.av_interleaved_write_frame(_format, _copy), "Mux HLS segment");
    }
    private void Open()
    {
        AVFormatContext* format = null;
        FFmpegRuntime.Check(ffmpeg.avformat_alloc_output_context2(&format, null, "mpegts", null), "Create TS muxer"); _format = format;
        AVStream* video = ffmpeg.avformat_new_stream(_format, null);
        AVStream* audio = ffmpeg.avformat_new_stream(_format, null);
        if (video == null || audio == null) throw new OutOfMemoryException();
        FFmpegRuntime.Check(ffmpeg.avcodec_parameters_from_context(video->codecpar, _video), "Copy video stream parameters");
        FFmpegRuntime.Check(ffmpeg.avcodec_parameters_from_context(audio->codecpar, _audio), "Copy audio stream parameters");
        video->time_base = _video->time_base; audio->time_base = _audio->time_base;
        AVIOContext* io = null;
        FFmpegRuntime.Check(ffmpeg.avio_open_dyn_buf(&io), "Allocate HLS segment buffer"); _format->pb = io;
        _format->flags |= ffmpeg.AVFMT_FLAG_CUSTOM_IO; _format->max_delay = 0;
        AVDictionary* options = null;
        ffmpeg.av_dict_set(&options, "mpegts_copyts", "1", 0);
        ffmpeg.av_dict_set(&options, "mpegts_flags", "+resend_headers", 0);
        try { FFmpegRuntime.Check(ffmpeg.avformat_write_header(_format, &options), "Write TS header"); }
        finally { ffmpeg.av_dict_free(&options); }
    }
    private void Finish(double end)
    {
        if (_format == null) return;
        try
        {
            FFmpegRuntime.Check(ffmpeg.av_write_trailer(_format), "Finish TS segment");
            byte* buffer = null;
            int length = ffmpeg.avio_close_dyn_buf(_format->pb, &buffer); _format->pb = null;
            try
            {
                FFmpegRuntime.Check(length, "Read completed TS segment");
                byte[] data = GC.AllocateUninitializedArray<byte>(length);
                Marshal.Copy((nint)buffer, data, 0, length);
                _store.Publish(data, Math.Max(0.001, end - _start));
            }
            finally { ffmpeg.av_free(buffer); }
        }
        finally { ReleaseContext(); }
    }
    private void ReleaseContext()
    {
        if (_format == null) return;
        if (_format->pb != null)
        {
            byte* buffer = null; ffmpeg.avio_close_dyn_buf(_format->pb, &buffer); ffmpeg.av_free(buffer);
        }
        ffmpeg.avformat_free_context(_format); _format = null;
    }
    public void Complete() { Finish(_end); _store.End(); }
    public void Dispose()
    {
        ReleaseContext(); AVPacket* copy = _copy; _copy = null; if (copy != null) ffmpeg.av_packet_free(&copy);
    }
}
