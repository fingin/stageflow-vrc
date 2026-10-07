using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace StageFlow.Engine.Media;

public static unsafe class FFmpegRuntime
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint AddDllDirectory(string directory);
    private static bool _initialized;
    private static readonly object Gate = new();
    public static string Version { get; private set; } = "Not loaded";
    public static void Initialize(string libraryDirectory)
    {
        lock (Gate)
        {
            if (_initialized) return;
            libraryDirectory = Path.GetFullPath(libraryDirectory);
            if (AddDllDirectory(libraryDirectory) == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            string[] libraries = ["avutil-60.dll", "swresample-6.dll", "swscale-9.dll", "avcodec-62.dll", "avformat-62.dll"];
            foreach (string library in libraries)
            {
                string path = Path.Combine(libraryDirectory, library);
                if (!File.Exists(path)) throw new FileNotFoundException($"Required media library is missing: {library}", path);
                NativeLibrary.Load(path);
            }
            ffmpeg.RootPath = libraryDirectory;
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);
            Check(ffmpeg.avformat_network_init(), "Initialize media network support");
            Version = ffmpeg.av_version_info();
            _initialized = true;
        }
    }
    public static int Check(int result, string operation)
    {
        if (result >= 0) return result;
        Span<byte> text = stackalloc byte[1024];
        fixed (byte* p = text)
        {
            ffmpeg.av_strerror(result, p, (ulong)text.Length);
            throw new InvalidOperationException($"{operation}: {Marshal.PtrToStringUTF8((nint)p)} ({result})");
        }
    }
}
