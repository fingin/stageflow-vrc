using FFmpeg.AutoGen;

namespace StageFlow.Engine.Media;

public sealed record MediaInfo(string Path, TimeSpan Duration, int Width, int Height, double VideoFps,
    string VideoCodec, string? AudioCodec, int AudioSampleRate, int AudioChannels);

public static unsafe class MediaProbe
{
    public static MediaInfo Read(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Media file not found.", path);
        AVFormatContext* format = null;
        try
        {
            FFmpegRuntime.Check(ffmpeg.avformat_open_input(&format, path, null, null), "Open media");
            FFmpegRuntime.Check(ffmpeg.avformat_find_stream_info(format, null), "Read media streams");
            int video = ffmpeg.av_find_best_stream(format, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
            if (video < 0) throw new InvalidDataException("The file has no supported video stream.");
            int audio = ffmpeg.av_find_best_stream(format, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0);
            var v = format->streams[video];
            var a = audio >= 0 ? format->streams[audio]->codecpar : null;
            var rate = ffmpeg.av_guess_frame_rate(format, v, null);
            double duration = format->duration == ffmpeg.AV_NOPTS_VALUE ? 0 : (double)format->duration / ffmpeg.AV_TIME_BASE;
            return new(path, TimeSpan.FromSeconds(Math.Max(0, duration)), v->codecpar->width, v->codecpar->height,
                rate.den == 0 ? 0 : (double)rate.num / rate.den, ffmpeg.avcodec_get_name(v->codecpar->codec_id),
                a == null ? null : ffmpeg.avcodec_get_name(a->codec_id), a == null ? 0 : a->sample_rate, a == null ? 0 : a->ch_layout.nb_channels);
        }
        finally { if (format != null) ffmpeg.avformat_close_input(&format); }
    }
}
