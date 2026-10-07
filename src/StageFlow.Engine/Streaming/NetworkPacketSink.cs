using System.Diagnostics;
using System.Threading.Channels;
using FFmpeg.AutoGen;
using StageFlow.Core.Projects;
using StageFlow.Engine.Media;

namespace StageFlow.Engine.Streaming;

/// <summary>Each network destination has an independent bounded queue and blocking-I/O worker.</summary>
public sealed unsafe class NetworkPacketSink : IDisposable
{
    private readonly Channel<(nint Packet, int Stream)> _queue = Channel.CreateBounded<(nint, int)>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private readonly NetworkStreamOutput _output;
    private readonly Thread _worker;
    private readonly AVRational[] _timeBases;
    private AVCodecParameters* _video, _audio;
    private volatile bool _cancel;
    private long _deadline;
    private string? _status;
    private readonly AVIOInterruptCB_callback _interrupt;
    public string Status => Volatile.Read(ref _status) ?? "Connecting";
    public string Name => _output.Name;
    public NetworkPacketSink(NetworkStreamOutput output, AVCodecContext* video, AVCodecContext* audio)
    {
        _output = output; _timeBases = [video->time_base, audio->time_base];
        try
        {
            _video = ffmpeg.avcodec_parameters_alloc(); _audio = ffmpeg.avcodec_parameters_alloc();
            if (_video == null || _audio == null) throw new OutOfMemoryException();
            FFmpegRuntime.Check(ffmpeg.avcodec_parameters_from_context(_video, video), "Copy network video parameters");
            FFmpegRuntime.Check(ffmpeg.avcodec_parameters_from_context(_audio, audio), "Copy network audio parameters");
        }
        catch
        {
            var v = _video; if (v != null) ffmpeg.avcodec_parameters_free(&v);
            var a = _audio; if (a != null) ffmpeg.avcodec_parameters_free(&a);
            throw;
        }
        _interrupt = _ => _cancel || Stopwatch.GetTimestamp() > Interlocked.Read(ref _deadline) ? 1 : 0;
        _worker = new Thread(Run) { IsBackground = true, Name = "StageFlow " + output.Protocol + " output" }; _worker.Start();
    }
    public void Write(AVPacket* packet, int stream)
    {
        if (_cancel) return;
        var copy = ffmpeg.av_packet_clone(packet); if (copy == null) throw new OutOfMemoryException();
        if (!_queue.Writer.TryWrite(((nint)copy, stream)))
        {
            ffmpeg.av_packet_free(&copy); _status = "Stopped: destination cannot keep up (queue full)"; _cancel = true; _queue.Writer.TryComplete();
        }
    }
    private void Deadline(int seconds = 3) => Interlocked.Exchange(ref _deadline, Stopwatch.GetTimestamp() + seconds * Stopwatch.Frequency);
    private void Run()
    {
        AVFormatContext* format = null;
        try
        {
            FFmpegRuntime.Check(ffmpeg.avformat_alloc_output_context2(&format, null, _output.Protocol == NetworkStreamProtocol.Rtmp ? "flv" : "mpegts", null), "Create network muxer");
            format->interrupt_callback = new AVIOInterruptCB { callback = _interrupt };
            var video = ffmpeg.avformat_new_stream(format, null); var audio = ffmpeg.avformat_new_stream(format, null);
            if (video == null || audio == null) throw new OutOfMemoryException();
            FFmpegRuntime.Check(ffmpeg.avcodec_parameters_copy(video->codecpar, _video), "Configure network video");
            FFmpegRuntime.Check(ffmpeg.avcodec_parameters_copy(audio->codecpar, _audio), "Configure network audio");
            video->time_base = _timeBases[0]; audio->time_base = _timeBases[1]; format->max_delay = 0;
            AVDictionary* options = null; ffmpeg.av_dict_set(&options, "rw_timeout", "3000000", 0);
            Deadline();
            try { FFmpegRuntime.Check(ffmpeg.avio_open2(&format->pb, _output.Url, ffmpeg.AVIO_FLAG_WRITE, &format->interrupt_callback, &options), "Connect network output"); }
            finally { ffmpeg.av_dict_free(&options); }
            if (_output.Protocol == NetworkStreamProtocol.Rtmp) ffmpeg.av_dict_set(&options, "flvflags", "no_duration_filesize", 0);
            else ffmpeg.av_dict_set(&options, "mpegts_flags", "+resend_headers", 0);
            try { Deadline(); FFmpegRuntime.Check(ffmpeg.avformat_write_header(format, &options), "Write network stream header"); }
            finally { ffmpeg.av_dict_free(&options); }
            _status = "Live";
            while (!_cancel && _queue.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            {
                while (!_cancel && _queue.Reader.TryRead(out var item))
                {
                    var packet = (AVPacket*)item.Packet;
                    try
                    {
                        packet->stream_index = item.Stream; ffmpeg.av_packet_rescale_ts(packet, _timeBases[item.Stream], format->streams[item.Stream]->time_base);
                        Deadline(); FFmpegRuntime.Check(ffmpeg.av_interleaved_write_frame(format, packet), "Send network media");
                    }
                    finally { ffmpeg.av_packet_free(&packet); }
                }
            }
            if (!_cancel) { Deadline(); ffmpeg.av_write_trailer(format); }
        }
        catch (Exception ex) { _status = "Stopped: " + ex.Message; }
        finally
        {
            _cancel = true; _queue.Writer.TryComplete();
            while (_queue.Reader.TryRead(out var item)) { var packet = (AVPacket*)item.Packet; ffmpeg.av_packet_free(&packet); }
            if (format != null) { if (format->pb != null) ffmpeg.avio_closep(&format->pb); ffmpeg.avformat_free_context(format); }
        }
    }
    public void Dispose()
    {
        _queue.Writer.TryComplete();
        if (!_worker.Join(500)) { _cancel = true; _worker.Join(); }
        var video = _video; _video = null; if (video != null) ffmpeg.avcodec_parameters_free(&video);
        var audio = _audio; _audio = null; if (audio != null) ffmpeg.avcodec_parameters_free(&audio);
        GC.KeepAlive(_interrupt);
    }
}
