using System.Text.Json;
using System.Text.Json.Serialization;

namespace StageFlow.Core.Projects;

public static class ProjectStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }, MaxDepth = 48
    };
    public static ShowProject Load(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > 16 * 1024 * 1024) throw new InvalidDataException("Project file is too large.");
        var project = JsonSerializer.Deserialize<ShowProject>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("Empty project.");
        project.Validate();
        if (project.MediaPath is { Length: > 0 } media && !Path.IsPathRooted(media))
            project = project with { MediaPath = Path.GetFullPath(media, Path.GetDirectoryName(Path.GetFullPath(path))!) };
        return project;
    }
    public static void Save(ShowProject project, string path)
    {
        project.Validate();
        path = Path.GetFullPath(path);
        string folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(folder);
        var saved = project.MediaPath is { Length: > 0 } media
            ? project with { MediaPath = Path.GetRelativePath(folder, media) } : project;
        string temp = Path.Combine(folder, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, saved, Options);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
