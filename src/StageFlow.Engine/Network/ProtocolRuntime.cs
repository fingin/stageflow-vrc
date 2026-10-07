using System.Reflection;
using System.Runtime.InteropServices;
namespace StageFlow.Engine.Network;
public static class ProtocolRuntime
{
    private static string? _directory;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, nint> Libraries = [];
    private static nint LoadOnce(string path)
    {
        lock (Gate)
        {
            if (!Libraries.TryGetValue(path, out var handle)) Libraries[path] = handle = NativeLibrary.Load(path);
            return handle;
        }
    }
    public static void Initialize(string directory)
    {
        lock (Gate)
        {
            if (_directory != null) return; _directory = Path.GetFullPath(directory);
            NativeLibrary.SetDllImportResolver(typeof(ProtocolRuntime).Assembly, Resolve);
            string omtFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StageFlowV2", "OMT");
            Environment.SetEnvironmentVariable("OMT_STORAGE_PATH", omtFolder);
        }
    }
    private static nint Resolve(string library, Assembly assembly, DllImportSearchPath? search)
    {
        string? name = library switch { "stageflow_omt" => "libomt.dll", "stageflow_ndi" => "Processing.NDI.Lib.x64.dll", _ => null };
        if (name == null) return 0;
        string path = Path.Combine(_directory!, name);
        // libomt's native-AOT P/Invoke resolver is separate from this assembly's.
        // Preload its codec by absolute path so it resolves without a global PATH change.
        if (library == "stageflow_omt" && File.Exists(Path.Combine(_directory!, "libvmx.dll"))) LoadOnce(Path.Combine(_directory!, "libvmx.dll"));
        if (File.Exists(path)) return LoadOnce(path);
        if (library == "stageflow_ndi")
            foreach (string env in new[] { "NDI_RUNTIME_DIR_V6", "NDI_RUNTIME_DIR_V5" })
            {
                string? dir = Environment.GetEnvironmentVariable(env);
                if (dir != null && File.Exists(Path.Combine(dir, name))) return LoadOnce(Path.Combine(dir, name));
            }
        throw new DllNotFoundException($"{name} is missing from the runtime folder.");
    }
}
internal interface ILanSender : IDisposable
{
    string Name { get; }
    string Protocol { get; }
    string Address { get; }
    void Send(LanFrame frame);
}
