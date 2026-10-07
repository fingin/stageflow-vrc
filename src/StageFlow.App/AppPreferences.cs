using System.ComponentModel;
using System.Text.Json;
namespace StageFlow.App;
internal enum AppSkin { Slate, Graphite, Midnight, Light }
internal enum AppAccent { Periwinkle, Cyan, Violet, Mint, Amber }
internal sealed record AppPreferences
{
    [Category("General"), DisplayName("Autosave interval (seconds)"), Description("Recovery copy interval. Your original show is saved only with Save.")]
    public int AutosaveSeconds { get; set; } = 30;
    [Category("General"), DisplayName("Offer recovery on startup")]
    public bool OfferRecoveryOnStartup { get; set; } = true;
    [Category("Appearance"), DisplayName("Skin")]
    public AppSkin Skin { get; set; } = AppSkin.Slate;
    [Category("Appearance"), DisplayName("Accent color")]
    public AppAccent Accent { get; set; } = AppAccent.Periwinkle;
    [Category("Appearance"), DisplayName("Interface text size"), Description("Text size in points. Applies immediately; does not affect video or timecode overlays.")]
    public int TextSize { get; set; } = 9;
    [Category("Appearance"), DisplayName("Compact layer rows")]
    public bool CompactRows { get; set; }
    [Category("Workspace"), DisplayName("Hierarchy width")]
    public int HierarchyWidth { get; set; } = 260;
    [Category("Workspace"), DisplayName("Inspector width")]
    public int InspectorWidth { get; set; } = 360;
    [Category("Workspace"), DisplayName("Show output status cards")]
    public bool ShowStatusCards { get; set; } = true;
    [Category("Workspace"), DisplayName("Show tooltips")]
    public bool ShowTooltips { get; set; } = true;
    [Category("Workspace"), DisplayName("Remember window size and position")]
    public bool RememberWindow { get; set; } = true;
    [Browsable(false)] public int WindowX { get; set; } = int.MinValue;
    [Browsable(false)] public int WindowY { get; set; }
    [Browsable(false)] public int WindowWidth { get; set; } = 1600;
    [Browsable(false)] public int WindowHeight { get; set; } = 1000;
    [Browsable(false)] public bool WindowMaximized { get; set; }
    [Category("Workspace"), DisplayName("Snap distance (pixels)"), Description("Hold Shift while dragging a layer to snap to this spacing.")]
    public int SnapPixels { get; set; } = 8;
    [Category("Diagnostics"), DisplayName("Record diagnostics on startup")]
    public bool RecordDiagnosticsOnStartup { get; set; }
    [Category("Diagnostics"), DisplayName("Status refresh (milliseconds)"), Description("Updates the interface only. Playback, MTC and streaming run independently.")]
    public int UiRefreshMilliseconds { get; set; } = 100;
    public void Normalize()
    {
        AutosaveSeconds = Math.Clamp(AutosaveSeconds, 5, 600); UiRefreshMilliseconds = Math.Clamp(UiRefreshMilliseconds, 33, 1000);
        HierarchyWidth = Math.Clamp(HierarchyWidth, 220, 500); InspectorWidth = Math.Clamp(InspectorWidth, 280, 620);
        TextSize = Math.Clamp(TextSize, 8, 13); SnapPixels = Math.Clamp(SnapPixels, 1, 128);
        WindowWidth = Math.Clamp(WindowWidth, 1100, 16384); WindowHeight = Math.Clamp(WindowHeight, 720, 16384);
        if (WindowX != int.MinValue) WindowX = Math.Clamp(WindowX, -65536, 65536); WindowY = Math.Clamp(WindowY, -65536, 65536);
        if (!Enum.IsDefined(Skin)) Skin = AppSkin.Slate; if (!Enum.IsDefined(Accent)) Accent = AppAccent.Periwinkle;
    }
    public static AppPreferences Load(string folder)
    {
        try { var preferences = JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(Path.Combine(folder, "preferences.json"))) ?? new(); preferences.Normalize(); return preferences; }
        catch { return new(); }
    }
    public void Save(string folder)
    {
        Normalize(); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "preferences.json"), temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, path, true);
    }
}
