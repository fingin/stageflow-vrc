using System.Runtime.InteropServices;
using System.Diagnostics;
using FFmpeg.AutoGen;
using StageFlow.Core.Projects;
using StageFlow.Engine.Graphics;
using StageFlow.Engine.Media;
using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;

namespace StageFlow.Engine.Streaming;

/// <summary>Single-thread native NVENC/AAC encoding, with GPU texture inputs and a continuous output clock.</summary>
public sealed unsafe class HardwareStreamEncoder : IDisposable
{
    private readonly GpuDevice _gpu;
    private AVCodecContext* _video;
    private AVCodecContext* _audio;
    private AVBufferRef* _device, _frames;
    private AVFrame* _videoFrame, _audioFrame;
    private AVPacket* _packet;
    private TsSegmentMuxer? _muxer;
    private readonly List<NetworkPacketSink> _network = [];
    private AVFrame* _softwareFrame;
    private readonly bool _software;
    public string NetworkStatus => string.Join(" · ", _network.Select(n => n.Name + ": " + n.Status));
    public StageFlow.Core.Monitoring.VideoRouteTelemetry[] NetworkTelemetry() => _network.Select(n => n.Snapshot()).ToArray();
    private GpuEncoderConverter? _converter;
    private long _videoPts, _audioPts;
    private int _audioFilled;
    private bool _completed;
    private readonly double[] _timings = new double[5];
    private long _submitted;
    public string TimingSummary => string.Join("/", _timings.Select(t => (t / Math.Max(1, _submitted)).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)));
    public long VideoFrames => _videoPts;
    public long AudioSamples => _audioPts;
    public HardwareStreamEncoder(GpuDevice gpu, HlsSegmentStore store, int width, int height, int fpsNum, int fpsDen, StreamSettings settings, IEnumerable<NetworkStreamOutput>? outputs = null)
    {
        settings.Validate();
        _gpu = gpu; _software = settings.Encoder == "libx264";
        try
        {
            AVCodec* videoCodec = ffmpeg.avcodec_find_encoder_by_name(settings.Encoder);
            if (videoCodec == null) throw new NotSupportedException($"Encoder {settings.Encoder} is unavailable.");
            _video = ffmpeg.avcodec_alloc_context3(videoCodec);
            if (_video == null) throw new OutOfMemoryException();
            _video->width = width; _video->height = height;
            _video->time_base = new AVRational { num = fpsDen, den = fpsNum };
            _video->framerate = new AVRational { num = fpsNum, den = fpsDen };
            _video->pix_fmt = _software ? AVPixelFormat.AV_PIX_FMT_NV12 : AVPixelFormat.AV_PIX_FMT_D3D11;
            _video->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
            if (_software) _video->thread_count = Math.Clamp(Environment.ProcessorCount / 4, 1, 8);
            bool qualityMode = settings.RateControl == VideoRateControl.ConstantQuality;
            long maximum = (settings.RateControl == VideoRateControl.Cbr || settings.MaxBitrateKbps == 0
                ? settings.VideoBitrateKbps : settings.MaxBitrateKbps) * 1000L;
            _video->bit_rate = qualityMode ? 0 : settings.VideoBitrateKbps * 1000L;
            _video->rc_min_rate = settings.RateControl == VideoRateControl.Cbr ? _video->bit_rate : 0;
            _video->rc_max_rate = qualityMode && settings.MaxBitrateKbps == 0 ? 0 : maximum;
            _video->rc_buffer_size = (int)Math.Round(_video->rc_max_rate * settings.BufferSeconds);
            _video->gop_size = settings.GopFrames(fpsNum, fpsDen);
            _video->max_b_frames = settings.BFrames;
            _video->colorspace = AVColorSpace.AVCOL_SPC_BT709;
            _video->color_primaries = AVColorPrimaries.AVCOL_PRI_BT709;
            _video->color_trc = AVColorTransferCharacteristic.AVCOL_TRC_BT709;
            // The D3D11 video processor supplies limited-range BT.709 NV12.
            _video->color_range = AVColorRange.AVCOL_RANGE_MPEG;
            _device = ffmpeg.av_hwdevice_ctx_alloc(AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA);
            if (_device == null) throw new OutOfMemoryException();
            Marshal.AddRef(gpu.Device.NativePointer);
            *(nint*)((AVHWDeviceContext*)_device->data)->hwctx = gpu.Device.NativePointer;
            FFmpegRuntime.Check(ffmpeg.av_hwdevice_ctx_init(_device), "Initialize encoder GPU device");
            _frames = ffmpeg.av_hwframe_ctx_alloc(_device);
            if (_frames == null) throw new OutOfMemoryException();
            var frameContext = (AVHWFramesContext*)_frames->data;
            frameContext->format = AVPixelFormat.AV_PIX_FMT_D3D11; frameContext->sw_format = AVPixelFormat.AV_PIX_FMT_NV12;
            frameContext->width = width; frameContext->height = height; frameContext->initial_pool_size = 0;
            // AVD3D11VAFramesContext.BindFlags follows its texture pointer in the public ABI.
            *(uint*)((byte*)frameContext->hwctx + IntPtr.Size) = (uint)Vortice.Direct3D11.BindFlags.RenderTarget;
            FFmpegRuntime.Check(ffmpeg.av_hwframe_ctx_init(_frames), "Create GPU encoder frame pool");
            _converter = new(gpu, width, height, fpsNum, fpsDen);
            if (!_software) _video->hw_frames_ctx = ffmpeg.av_buffer_ref(_frames);
            AVDictionary* options = null;
            string preset = settings.Preset switch { EncodingPreset.Balanced => _software ? "faster" : "p4", EncodingPreset.Quality => _software ? "medium" : "p6", _ => _software ? "veryfast" : "p3" };
            ffmpeg.av_dict_set(&options, "preset", preset, 0);
            if (settings.Encoder != "hevc_nvenc" && settings.Profile != H264Profile.Auto)
                ffmpeg.av_dict_set(&options, "profile", settings.Profile.ToString().ToLowerInvariant(), 0);
            if (_software)
            {
                if (settings.BFrames == 0) ffmpeg.av_dict_set(&options, "tune", "zerolatency", 0);
                ffmpeg.av_dict_set(&options, "forced-idr", "1", 0);
                ffmpeg.av_dict_set(&options, "sc_threshold", "0", 0);
                ffmpeg.av_dict_set(&options, "x264-params", "open-gop=0:rc-lookahead=0:sync-lookahead=0" + (settings.RateControl == VideoRateControl.Cbr ? ":nal-hrd=cbr:filler=1" : ""), 0);
                if (qualityMode) ffmpeg.av_dict_set_int(&options, "crf", settings.Quality, 0);
            }
            if (!_software)
            {
                ffmpeg.av_dict_set(&options, "tune", settings.BFrames == 0 ? "ull" : "hq", 0);
                ffmpeg.av_dict_set(&options, "rc", settings.RateControl == VideoRateControl.Cbr ? "cbr" : "vbr", 0);
                ffmpeg.av_dict_set(&options, "zerolatency", settings.BFrames == 0 ? "1" : "0", 0);
                ffmpeg.av_dict_set(&options, "no-scenecut", "1", 0);
                if (qualityMode) ffmpeg.av_dict_set_int(&options, "cq", settings.Quality, 0);
                ffmpeg.av_dict_set(&options, "forced-idr", "1", 0);
            }
            try
            {
                FFmpegRuntime.Check(ffmpeg.avcodec_open2(_video, videoCodec, &options), "Open video encoder");
                if (ffmpeg.av_dict_count(options) != 0) throw new InvalidOperationException("The video encoder did not accept all selected settings.");
            }
            finally { ffmpeg.av_dict_free(&options); }
            AVCodec* audioCodec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_AAC);
            if (audioCodec == null) throw new NotSupportedException("AAC encoder is unavailable.");
            _audio = ffmpeg.avcodec_alloc_context3(audioCodec);
            if (_audio == null) throw new OutOfMemoryException();
            _audio->sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_FLTP;
            _audio->sample_rate = 48000; _audio->time_base = new AVRational { num = 1, den = 48000 };
            _audio->bit_rate = settings.AudioBitrateKbps * 1000L;
            ffmpeg.av_channel_layout_default(&_audio->ch_layout, 2);
            FFmpegRuntime.Check(ffmpeg.avcodec_open2(_audio, audioCodec, null), "Open stream audio encoder");
            _videoFrame = ffmpeg.av_frame_alloc(); _audioFrame = ffmpeg.av_frame_alloc(); _packet = ffmpeg.av_packet_alloc(); _softwareFrame = ffmpeg.av_frame_alloc();
            if (_videoFrame == null || _audioFrame == null || _packet == null || _softwareFrame == null) throw new OutOfMemoryException();
            _audioFrame->format = (int)_audio->sample_fmt; _audioFrame->sample_rate = 48000;
            _audioFrame->nb_samples = _audio->frame_size;
            FFmpegRuntime.Check(ffmpeg.av_channel_layout_copy(&_audioFrame->ch_layout, &_audio->ch_layout), "Copy stream channel layout");
            FFmpegRuntime.Check(ffmpeg.av_frame_get_buffer(_audioFrame, 0), "Allocate stream audio frame");
            if (settings.HlsEnabled) _muxer = new TsSegmentMuxer(store, _video, _audio, settings.SegmentSeconds);
            foreach (var output in outputs ?? []) if (output.Enabled) _network.Add(new(output, _video, _audio));
        }
        catch { Dispose(); throw; }
    }
    public void SubmitVideo(GpuSurface surface, long? presentationFrame = null)
    {
        long time = Stopwatch.GetTimestamp();
        if (_completed) throw new InvalidOperationException("Encoder is completed.");
        if (surface.Width != _video->width || surface.Height != _video->height) throw new ArgumentException("Encoder texture dimensions differ.");
        ffmpeg.av_frame_unref(_videoFrame);
        FFmpegRuntime.Check(ffmpeg.av_hwframe_get_buffer(_frames, _videoFrame, 0), "Acquire GPU encoder surface");
        Profile(0, ref time);
        nint pointer = (nint)_videoFrame->data[0]; Marshal.AddRef(pointer);
        using (var destination = new ID3D11Texture2D(pointer))
            _converter!.Convert(surface.Texture, destination);
        _gpu.Context.Flush();
        Profile(1, ref time);
        long pts = presentationFrame ?? _videoPts;
        if (pts < _videoPts) throw new ArgumentException("Video timestamps must increase.");
        bool key = pts == 0 || pts / _video->gop_size > Math.Max(0, _videoPts - 1) / _video->gop_size;
        _videoFrame->pts = pts; _videoPts = pts + 1;
        _videoFrame->pict_type = key ? AVPictureType.AV_PICTURE_TYPE_I : AVPictureType.AV_PICTURE_TYPE_NONE;
        _videoFrame->color_range = _video->color_range; _videoFrame->colorspace = _video->colorspace;
        AVFrame* encodeFrame = _videoFrame;
        if (_software)
        {
            ffmpeg.av_frame_unref(_softwareFrame);
            FFmpegRuntime.Check(ffmpeg.av_hwframe_transfer_data(_softwareFrame, _videoFrame, 0), "Transfer frame for software encoder");
            FFmpegRuntime.Check(ffmpeg.av_frame_copy_props(_softwareFrame, _videoFrame), "Copy software frame properties"); encodeFrame = _softwareFrame;
        }
        FFmpegRuntime.Check(ffmpeg.avcodec_send_frame(_video, encodeFrame), "Submit video to encoder");
        Profile(2, ref time);
        DrainVideo();
        Profile(3, ref time); _submitted++;
    }
    public void SubmitAudio(ReadOnlySpan<float> interleavedStereo, float gain = 1)
    {
        long time = Stopwatch.GetTimestamp();
        if ((interleavedStereo.Length & 1) != 0) throw new ArgumentException("Stereo samples must be paired.");
        if (_completed) throw new InvalidOperationException("Encoder is completed.");
        for (int i = 0; i < interleavedStereo.Length; i += 2)
        {
            if (_audioFilled == 0) FFmpegRuntime.Check(ffmpeg.av_frame_make_writable(_audioFrame), "Acquire audio encode buffer");
            ((float*)_audioFrame->data[0])[_audioFilled] = Math.Clamp(interleavedStereo[i] * gain, -1, 1);
            ((float*)_audioFrame->data[1])[_audioFilled++] = Math.Clamp(interleavedStereo[i + 1] * gain, -1, 1);
            if (_audioFilled == _audio->frame_size) SendAudioFrame();
        }
        Profile(4, ref time);
    }
    private void Profile(int index, ref long start)
    { long now = Stopwatch.GetTimestamp(); _timings[index] += (now - start) * 1000.0 / Stopwatch.Frequency; start = now; }
    private void SendAudioFrame()
    {
        _audioFrame->pts = _audioPts; _audioPts += _audioFrame->nb_samples;
        FFmpegRuntime.Check(ffmpeg.avcodec_send_frame(_audio, _audioFrame), "Submit stream audio");
        _audioFilled = 0; DrainAudio();
    }
    private void DrainVideo()
    {
        while (true)
        {
            int result = ffmpeg.avcodec_receive_packet(_video, _packet);
            if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN) || result == ffmpeg.AVERROR_EOF) break;
            FFmpegRuntime.Check(result, "Read encoded video");
            _muxer?.WriteVideo(_packet); foreach (var output in _network) output.Write(_packet, 0); ffmpeg.av_packet_unref(_packet);
        }
    }
    private void DrainAudio()
    {
        while (true)
        {
            int result = ffmpeg.avcodec_receive_packet(_audio, _packet);
            if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN) || result == ffmpeg.AVERROR_EOF) break;
            FFmpegRuntime.Check(result, "Read encoded audio");
            _muxer?.WriteAudio(_packet); foreach (var output in _network) output.Write(_packet, 1); ffmpeg.av_packet_unref(_packet);
        }
    }
    public void Complete()
    {
        if (_completed) return;
        if (_audioFilled > 0)
        {
            new Span<float>((float*)_audioFrame->data[0] + _audioFilled, _audio->frame_size - _audioFilled).Clear();
            new Span<float>((float*)_audioFrame->data[1] + _audioFilled, _audio->frame_size - _audioFilled).Clear(); SendAudioFrame();
        }
        FFmpegRuntime.Check(ffmpeg.avcodec_send_frame(_video, null), "Flush video encoder"); DrainVideo();
        FFmpegRuntime.Check(ffmpeg.avcodec_send_frame(_audio, null), "Flush audio encoder"); DrainAudio();
        _muxer?.Complete(); _completed = true;
    }
    public void Dispose()
    {
        _muxer?.Dispose(); _muxer = null;
        foreach (var output in _network) output.Dispose(); _network.Clear();
        var sf = _softwareFrame; _softwareFrame = null; if (sf != null) ffmpeg.av_frame_free(&sf);
        _converter?.Dispose(); _converter = null;
        var vf = _videoFrame; _videoFrame = null; if (vf != null) ffmpeg.av_frame_free(&vf);
        var af = _audioFrame; _audioFrame = null; if (af != null) ffmpeg.av_frame_free(&af);
        var p = _packet; _packet = null; if (p != null) ffmpeg.av_packet_free(&p);
        var v = _video; _video = null; if (v != null) ffmpeg.avcodec_free_context(&v);
        var a = _audio; _audio = null; if (a != null) ffmpeg.avcodec_free_context(&a);
        var f = _frames; _frames = null; if (f != null) ffmpeg.av_buffer_unref(&f);
        var d = _device; _device = null; if (d != null) ffmpeg.av_buffer_unref(&d);
    }
}
