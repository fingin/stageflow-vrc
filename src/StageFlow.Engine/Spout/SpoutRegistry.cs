using System.IO.MemoryMappedFiles;
using System.Text;
using Microsoft.Win32;

namespace StageFlow.Engine.Spout;

/// <summary>Spout2's documented 256-byte sender names and 280-byte shared texture metadata.</summary>
public static class SpoutRegistry
{
    internal const string MapName = "SpoutSenderNames";
    internal static int MaxSenders => Math.Clamp((int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Leading Edge\Spout", "MaxSenders", 64) ?? 64, 10, 4096);
    internal static bool Enter(Mutex mutex, int timeout = 0)
    { try { return mutex.WaitOne(timeout); } catch (AbandonedMutexException) { return true; } }
    public static IReadOnlyList<string> Senders()
    {
        try
        {
            using var map = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
            using var view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            using var mutex = new Mutex(false, MapName + "_mutex");
            if (!Enter(mutex, 10)) return [];
            try { return ReadNames(view); } finally { mutex.ReleaseMutex(); }
        }
        catch (FileNotFoundException) { return []; }
    }
    internal static List<string> ReadNames(MemoryMappedViewAccessor view)
    {
        var names = new List<string>(); byte[] buffer = new byte[256];
        for (long offset = 0; offset + 256 <= Math.Min(view.Capacity, MaxSenders * 256L); offset += 256)
        {
            view.ReadArray(offset, buffer, 0, 256); if (buffer[0] == 0) break;
            int length = Array.IndexOf(buffer, (byte)0); if (length < 0) continue;
            names.Add(Encoding.Latin1.GetString(buffer, 0, length));
        }
        return names;
    }
    internal static void WriteNames(MemoryMappedViewAccessor view, IEnumerable<string> names)
    {
        long offset = 0; byte[] buffer = new byte[256];
        foreach (string name in names.Order(StringComparer.Ordinal))
        {
            if (offset + 256 > Math.Min(view.Capacity, MaxSenders * 256L)) throw new InvalidOperationException("Spout sender registry is full.");
            Array.Clear(buffer); Encoding.Latin1.GetBytes(name, buffer); view.WriteArray(offset, buffer, 0, buffer.Length); offset += 256;
        }
        if (offset < view.Capacity) view.Write(offset, (byte)0);
    }
    internal static (uint Handle, int Width, int Height, uint Format)? ReadInfo(string name)
    {
        try
        {
            // Close immediately: retaining a sender's map would prevent disappearance detection.
            using var map = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
            using var view = map.CreateViewAccessor(0, 280, MemoryMappedFileAccess.Read);
            using var mutex = new Mutex(false, name + "_mutex");
            if (!Enter(mutex)) return null;
            try { return (view.ReadUInt32(0), view.ReadInt32(4), view.ReadInt32(8), view.ReadUInt32(12)); }
            finally { mutex.ReleaseMutex(); }
        }
        catch (FileNotFoundException) { return null; }
    }
}
