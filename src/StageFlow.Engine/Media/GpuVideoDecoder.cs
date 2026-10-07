using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using StageFlow.Engine.Graphics;

namespace StageFlow.Engine.Media;

/// <summary>
/// Native FFmpeg demux/decode with output retained as D3D11 textures. Single-thread owner.
/// Frame pointer is valid until the next Read or Seek; consumers needing longer ownership must av_frame_ref.
/// </summary>
public sealed unsafe class GpuVideoDecoder : IDisposable
{
    private AVFormatContext* _format;
    private AVCodecContext* _codec;
    private AVBufferRef* _hardware;
    private AVPacket* _packet;
    private AVFrame* _frame;
    private int _stream;
    private bool _draining;
    private AVRational _timeBase;
    private long _startTime;
    public int Width => _codec->width;
    public int Height => _codec->height;
    public AVFrame* Frame => _frame;
    public TimeSpan Position { get; private set; }
    public GpuVideoDecoder(GpuDevice gpu, string path)
    {
        try
        {
            AVFormatContext* format = null;
            FFmpegRuntime.Check(ffmpeg.avformat_open_input(&format, path, null, null), "Open video"); _format = format;
            FFmpegRuntime.Check(ffmpeg.avformat_find_stream_info(_format, null), "Inspect video");
            AVCodec* decoder = null;
            _stream = FFmpegRuntime.Check(ffmpeg.av_find_best_stream(_format, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &decoder, 0), "Find video stream");
            var stream = _format->streams[_stream]; _timeBase = stream->time_base;
            _startTime = stream->start_time == ffmpeg.AV_NOPTS_VALUE ? 0 : stream->start_time;
            _codec = ffmpeg.avcodec_alloc_context3(decoder);
            if (_codec == null) throw new OutOfMemoryException();
            FFmpegRuntime.Check(ffmpeg.avcodec_parameters_to_context(_codec, stream->codecpar), "Configure decoder");
            _hardware = ffmpeg.av_hwdevice_ctx_alloc(AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA);
            if (_hardware == null) throw new NotSupportedException("D3D11 video decoding is unavailable.");
            var context = (AVHWDeviceContext*)_hardware->data;
            // First field of the public AVD3D11VADeviceContext ABI is ID3D11Device*.
            // FFmpeg owns the reference supplied here and releases it on buffer unref.
            Marshal.AddRef(gpu.Device.NativePointer);
            *(nint*)context->hwctx = gpu.Device.NativePointer;
            FFmpegRuntime.Check(ffmpeg.av_hwdevice_ctx_init(_hardware), "Initialize shared decoder device");
            _codec->hw_device_ctx = ffmpeg.av_buffer_ref(_hardware);
            _codec->thread_count = 1; _codec->extra_hw_frames = 8;
            FFmpegRuntime.Check(ffmpeg.avcodec_open2(_codec, decoder, null), "Open hardware decoder");
            _packet = ffmpeg.av_packet_alloc(); _frame = ffmpeg.av_frame_alloc();
            if (_packet == null || _frame == null) throw new OutOfMemoryException();
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
                if (_frame->format != (int)AVPixelFormat.AV_PIX_FMT_D3D11)
                    throw new NotSupportedException($"This media did not produce GPU frames (pixel format {(AVPixelFormat)_frame->format}).");
                long timestamp = _frame->best_effort_timestamp;
                Position = timestamp == ffmpeg.AV_NOPTS_VALUE ? Position :
                    TimeSpan.FromTicks(ffmpeg.av_rescale_q(timestamp - _startTime, _timeBase, new AVRational { num = 1, den = (int)TimeSpan.TicksPerSecond }));
                return true;
            }
            if (result == ffmpeg.AVERROR_EOF) return false;
            if (result != ffmpeg.AVERROR(ffmpeg.EAGAIN)) FFmpegRuntime.Check(result, "Decode video frame");
            if (_draining) return false;
            do
            {
                ffmpeg.av_packet_unref(_packet);
                result = ffmpeg.av_read_frame(_format, _packet);
            } while (result >= 0 && _packet->stream_index != _stream);
            if (result == ffmpeg.AVERROR_EOF)
            { FFmpegRuntime.Check(ffmpeg.avcodec_send_packet(_codec, null), "Drain video decoder"); _draining = true; }
            else
            {
                FFmpegRuntime.Check(result, "Read video packet");
                FFmpegRuntime.Check(ffmpeg.avcodec_send_packet(_codec, _packet), "Submit video packet");
                ffmpeg.av_packet_unref(_packet);
            }
        }
    }
    public void Seek(TimeSpan position)
    {
        long timestamp = ffmpeg.av_rescale_q(position.Ticks, new AVRational { num = 1, den = (int)TimeSpan.TicksPerSecond }, _timeBase) + _startTime;
        FFmpegRuntime.Check(ffmpeg.av_seek_frame(_format, _stream, timestamp, ffmpeg.AVSEEK_FLAG_BACKWARD), "Seek video");
        ffmpeg.avcodec_flush_buffers(_codec); ffmpeg.av_frame_unref(_frame); ffmpeg.av_packet_unref(_packet);
        _draining = false;
    }
    public void Dispose()
    {
        AVFrame* frame = _frame; _frame = null; if (frame != null) ffmpeg.av_frame_free(&frame);
        AVPacket* packet = _packet; _packet = null; if (packet != null) ffmpeg.av_packet_free(&packet);
        AVCodecContext* codec = _codec; _codec = null; if (codec != null) ffmpeg.avcodec_free_context(&codec);
        AVBufferRef* hardware = _hardware; _hardware = null; if (hardware != null) ffmpeg.av_buffer_unref(&hardware);
        AVFormatContext* format = _format; _format = null; if (format != null) ffmpeg.avformat_close_input(&format);
    }
}
