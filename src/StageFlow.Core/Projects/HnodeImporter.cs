using System.Globalization;
using StageFlow.Core.Grid;
using YamlDotNet.RepresentationModel;

namespace StageFlow.Core.Projects;

public sealed record HnodeImportResult(ShowProject Project, IReadOnlyList<string> Notices);

/// <summary>Reads data-only YAML nodes. Never activates CLR types based on YAML tags.</summary>
public static class HnodeImporter
{
    public static HnodeImportResult Import(string yaml, string name = "Imported Hnode show")
    {
        if (yaml.Length > 2 * 1024 * 1024) throw new InvalidDataException("Hnode configuration exceeds 2 MB.");
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root)
            throw new InvalidDataException("Expected one Hnode mapping document.");
        var notices = new List<string>();
        string res = Text(root, "outputResolution", "1920x1080");
        string[] dimensions = res.Split('x');
        if (dimensions.Length != 2 || !int.TryParse(dimensions[0], out int width) || !int.TryParse(dimensions[1], out int height))
            throw new InvalidDataException("Invalid Hnode output resolution.");
        YamlMappingNode serializer = Node(root, "serializer") as YamlMappingNode ?? throw new InvalidDataException("Missing Hnode serializer.");
        string tag = serializer.Tag.ToString().TrimStart('!');
        GridEncoding encoding = tag switch
        {
            "VRSL" => GridEncoding.Vrsl, "MDMX" or "BinaryStageFlight" => GridEncoding.BinaryStageFlight,
            "Binary" => GridEncoding.Binary, "Ternary" => GridEncoding.Ternary,
            _ => throw new InvalidDataException($"The Hnode serializer '{tag}' is not supported yet; import was not applied.")
        };
        if (!Enum.TryParse(Text(serializer, "outputConfig", "HorizontalTop"), out GridOrientation orientation))
            throw new InvalidDataException("Unknown Hnode grid orientation.");
        bool rgb = Boolean(serializer, "rGBGridMode", false);
        int capacity = encoding switch
        {
            GridEncoding.BinaryStageFlight => Math.Max(1, (width / 4) * 6 * (height / 208) / 512),
            GridEncoding.Binary => Math.Max(1, width / 4 * 52 / 8 / 512),
            GridEncoding.Ternary => Math.Max(1, width / 4 * 8 / 512),
            _ => rgb ? 9 : Math.Max(1, (orientation is GridOrientation.VerticalLeft or GridOrientation.VerticalRight ? height : width) / 640)
        };
        int requested = Integer(root, "serializeUniverseCount", int.MaxValue);
        int universes = Math.Clamp(Math.Min(capacity, requested), 1, 512);
        if (requested != int.MaxValue && requested > capacity)
            notices.Add($"Requested {requested} universes exceed the canvas capacity of {capacity}; the visible grid is limited to {universes}.");
        var masks = new List<ChannelRange>();
        if (Node(root, "maskedChannels") is YamlSequenceNode ranges)
            foreach (var item in ranges)
            {
                if (item is not YamlMappingNode range) throw new InvalidDataException("Invalid Hnode mask.");
                masks.Add(new(ParseChannel(Text(range, "start", "0")), ParseChannel(Text(range, "end", "0"))));
            }
        var layout = new GridLayout
        {
            Encoding = encoding, Width = width, Height = height, UniverseCount = universes,
            Orientation = orientation, GammaCorrection = Boolean(serializer, "gammaCorrection", true), RgbPacking = rgb,
            InvertMask = Boolean(root, "invertMask", false), MaskZero = Boolean(root, "autoMaskOnZero", false), Masks = masks
        };
        var midi = new MidiDmxSettings();
        if (Node(root, "exporters") is YamlSequenceNode exporters)
            foreach (var item in exporters)
            {
                if (item is YamlMappingNode exporter && exporter.Tag.ToString() == "!MIDIDMX")
                    midi = new() { Enabled = true, OutputDevice = Text(exporter, "midiDevice", "loopMIDI Port"),
                        ChannelsPerUpdate = Integer(exporter, "channelsPerUpdate", 100), IdleScanChannels = Integer(exporter, "idleScanChannels", 10) };
                else notices.Add($"Exporter {item.Tag} was preserved in the original YAML but is not active.");
            }
        if (Boolean(root, "transcode", false))
            throw new InvalidDataException("This configuration requires Spout grid transcoding, which is not implemented yet. Import was not applied.");
        if (Node(root, "generators") is YamlSequenceNode generators && generators.Children.Count != 0)
            throw new InvalidDataException("This configuration has DMX generators which are not implemented yet. Import was not applied.");
        var project = new ShowProject
        {
            Name = name, Width = width, Height = height, FpsNumerator = Integer(root, "targetFramerate", 60),
            ArtNet = new() { BindAddress = Text(root, "artNetAddress", "0.0.0.0"), Port = Integer(root, "artNetPort", 6454),
                UniverseCount = universes, AutomaticUniverseCount = requested == int.MaxValue, Enabled = true },
            MidiDmx = midi,
            Layers = [new() { Name = "Movie", Kind = LayerKind.Video, Width = width, Height = height },
                new() { Name = $"Art-Net · {tag}", Kind = LayerKind.Grid, Width = width, Height = height, Grid = layout, Locked = true }],
            ImportedHnodeConfiguration = yaml
        };
        project.Validate();
        notices.Add($"Imported {tag}, {width}×{height}, {project.FpsNumerator} fps. Art-Net universe addresses are zero-based; Hnode channel notation is one-based.");
        return new(project, notices);
    }
    private static YamlNode? Node(YamlMappingNode node, string key) => node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;
    private static string Text(YamlMappingNode node, string key, string fallback) => (Node(node, key) as YamlScalarNode)?.Value ?? fallback;
    private static int Integer(YamlMappingNode node, string key, int fallback) =>
        int.TryParse(Text(node, key, fallback.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value : throw new InvalidDataException($"Invalid integer for {key}.");
    private static bool Boolean(YamlMappingNode node, string key, bool fallback) =>
        bool.TryParse(Text(node, key, fallback.ToString()), out bool value) ? value : throw new InvalidDataException($"Invalid boolean for {key}.");
    public static int ParseChannel(string text)
    {
        var parts = text.Split('.');
        if (parts.Length == 1 && int.TryParse(parts[0], out int absolute) && absolute >= 0) return absolute;
        if (parts.Length == 2 && int.TryParse(parts[0], out int universe) && int.TryParse(parts[1], out int channel) &&
            universe is >= 1 and <= 512 && channel is >= 1 and <= 512) return (universe - 1) * 512 + channel - 1;
        throw new InvalidDataException($"Invalid channel '{text}'. Use a nonnegative index or universe.channel; expressions are not supported yet.");
    }
}
