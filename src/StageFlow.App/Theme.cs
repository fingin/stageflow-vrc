namespace StageFlow.App;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(13, 17, 24);
    public static readonly Color Panel = Color.FromArgb(21, 28, 39);
    public static readonly Color Input = Color.FromArgb(31, 41, 56);
    public static readonly Color Text = Color.FromArgb(230, 237, 248);
    public static readonly Color Muted = Color.FromArgb(151, 165, 185);
    public static readonly Color Accent = Color.FromArgb(112, 214, 187);
    public static readonly Color Border = Color.FromArgb(45, 59, 77);
    public static Button Button(string text, EventHandler? click = null, bool primary = false)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 34, Padding = new Padding(10, 4, 10, 4),
            FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Input, ForeColor = primary ? Background : Text,
            Cursor = Cursors.Hand, Margin = new Padding(3), Font = new Font("Segoe UI", 9) };
        button.FlatAppearance.BorderColor = Border;
        if (click != null) button.Click += click;
        return button;
    }
    public static Label Label(string text, bool muted = false) => new() { Text = text, AutoSize = true, ForeColor = muted ? Muted : Text,
        BackColor = Color.Transparent, Margin = new Padding(3, 8, 3, 4) };
    public static TextBox TextBox(string text = "") => new() { Text = text, BackColor = Input, ForeColor = Text, BorderStyle = BorderStyle.FixedSingle, Width = 160 };
    public static void Apply(Control control)
    {
        control.BackColor = Panel; control.ForeColor = Text;
        foreach (Control child in control.Controls) Apply(child);
        if (control is System.Windows.Forms.TextBox or ComboBox or NumericUpDown or ListBox or PropertyGrid) control.BackColor = Input;
    }
}
