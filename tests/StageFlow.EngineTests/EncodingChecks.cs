using FFmpeg.AutoGen;
using StageFlow.Core.Projects;
using StageFlow.Engine.Graphics;
using StageFlow.Engine.Media;
using StageFlow.Engine.Streaming;
using Vortice.Mathematics;

internal static unsafe class EncodingChecks
{
    public static void Run(string nativeDirectory, string directory)
    {
        FFmpegRuntime.Initialize(nativeDirectory); Directory.CreateDirectory(directory);
        using var gpu = new GpuDevice(); using var surface = new GpuSurface(gpu, 640, 360);
        foreach (string codec in new[] { "h264_nvenc", "hevc_nvenc", "libx264" })
        foreach (var (mode, uncapped) in new[] { (VideoRateControl.Cbr, false), (VideoRateControl.Vbr, false), (VideoRateControl.ConstantQuality, false), (VideoRateControl.ConstantQuality, true) })
        {
            var settings = new StreamSettings { Encoder = codec, RateControl = mode, VideoBitrateKbps = 2000, MaxBitrateKbps = mode == VideoRateControl.Cbr || uncapped ? 0 : 3000,
                KeyframeSeconds = 2, SegmentSeconds = 1, BFrames = mode == VideoRateControl.Cbr ? 0 : 2,
                Profile = mode == VideoRateControl.Cbr ? H264Profile.Baseline : mode == VideoRateControl.Vbr ? H264Profile.Main : H264Profile.High,
                Preset = (EncodingPreset)(int)mode };
            var store = new HlsSegmentStore(targetSeconds: settings.HlsTargetSeconds(30, 1));
            using (var encoder = new HardwareStreamEncoder(gpu, store, 640, 360, 30, 1, settings))
            {
                float[] audio = new float[3200];
                for (int frame = 0; frame < 180; frame++)
                {
                    gpu.Context.ClearRenderTargetView(surface.Target, new Color4((frame % 61) / 61f, 0.2f, 0.7f, 1));
                    for (int sample = 0; sample < 1600; sample++) audio[sample * 2] = audio[sample * 2 + 1] = (float)Math.Sin((frame * 1600L + sample) * Math.PI * 880 / 48000) * 0.1f;
                    encoder.SubmitVideo(surface); encoder.SubmitAudio(audio);
                }
                encoder.Complete();
            }
            string path = Path.Combine(directory, codec + "-" + mode + (uncapped ? "-uncapped" : "") + ".ts");
            using (var file = File.Create(path)) for (int sequence = 0; sequence < store.SegmentCount; sequence++) file.Write(store.Get(sequence)!.Data);
            CheckPackets(path, codec, settings);
            File.WriteAllText(Path.ChangeExtension(path, ".m3u8"), store.Playlist());
            if (store.SegmentCount != 3) throw new Exception("Expected three two-second HLS segments.");
            Console.WriteLine($"PASS {codec} {mode}{(uncapped ? " uncapped" : "")}: profile {(codec == "hevc_nvenc" ? "HEVC Main" : settings.Profile)}, preset {settings.Preset}, B={settings.BFrames}, 2-second GOP, HLS + AAC");
        }
    }
    private static void CheckPackets(string path, string codec, StreamSettings settings)
    {
        AVFormatContext* format = null; AVPacket* packet = null;
        try
        {
            FFmpegRuntime.Check(ffmpeg.avformat_open_input(&format, path, null, null), "Read encoded settings fixture");
            FFmpegRuntime.Check(ffmpeg.avformat_find_stream_info(format, null), "Probe encoded settings fixture");
            int video = ffmpeg.av_find_best_stream(format, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
            int audio = ffmpeg.av_find_best_stream(format, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0);
            if (video < 0 || audio < 0 || format->streams[audio]->codecpar->codec_id != AVCodecID.AV_CODEC_ID_AAC) throw new Exception("Encoded program is missing video/AAC.");
            var parameters = format->streams[video]->codecpar;
            int profile = settings.Profile switch { H264Profile.Baseline => 66, H264Profile.Main => 77, _ => 100 };
            if (codec != "hevc_nvenc" && (parameters->profile & 255) != profile) throw new Exception("Encoded H.264 profile does not match selection: " + parameters->profile);
            packet = ffmpeg.av_packet_alloc(); int frames = 0, audioPackets = 0; long lastDts = long.MinValue; double? previousKey = null; bool reordered = false;
            while (ffmpeg.av_read_frame(format, packet) >= 0)
            {
                if (packet->stream_index == video)
                {
                    frames++; if (packet->dts <= lastDts) throw new Exception("Video DTS did not increase."); lastDts = packet->dts;
                    reordered |= packet->pts != packet->dts;
                    if ((packet->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0)
                    {
                        var timeBase = format->streams[video]->time_base; double time = packet->pts * (double)timeBase.num / timeBase.den;
                        if (previousKey.HasValue && Math.Abs(time - previousKey.Value - 2) > 0.04) throw new Exception("Keyframe interval was not two seconds.");
                        previousKey = time;
                    }
                }
                else if (packet->stream_index == audio) audioPackets++;
                ffmpeg.av_packet_unref(packet);
            }
            if (frames != 180 || audioPackets < 280 || settings.BFrames > 0 && !reordered) throw new Exception($"Unexpected encoded packets video={frames}, audio={audioPackets}, reordered={reordered}");
        }
        finally { if (packet != null) ffmpeg.av_packet_free(&packet); if (format != null) ffmpeg.avformat_close_input(&format); }
    }
}
