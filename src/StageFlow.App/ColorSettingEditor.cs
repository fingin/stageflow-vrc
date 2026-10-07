using System.ComponentModel;
using System.Drawing.Design;
using System.Globalization;
namespace StageFlow.App;

// Keep ARGB text editable for precise alpha; the picker changes RGB only.
internal sealed class ColorSettingEditor : UITypeEditor
{
    public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.Modal;
    public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider provider, object? value)
    {
        Color current = Parse(value);
        using var dialog = new ColorDialog { Color = Color.FromArgb(current.R, current.G, current.B), FullOpen = true, AnyColor = true };
        if (dialog.ShowDialog(Form.ActiveForm) != DialogResult.OK) return value;
        return $"#{current.A:X2}{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
    }
    public override bool GetPaintValueSupported(ITypeDescriptorContext? context) => true;
    public override void PaintValue(PaintValueEventArgs e)
    {
        e.Graphics.FillRectangle(Brushes.White, e.Bounds);
        e.Graphics.FillRectangle(Brushes.Gray, e.Bounds.X, e.Bounds.Y, e.Bounds.Width / 2, e.Bounds.Height);
        using var brush = new SolidBrush(Parse(e.Value)); e.Graphics.FillRectangle(brush, e.Bounds);
    }
    private static Color Parse(object? value) => value is string text && uint.TryParse(text.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint color)
        ? Color.FromArgb(unchecked((int)(text.TrimStart('#').Length == 6 ? color | 0xFF000000 : color))) : Color.White;
}
