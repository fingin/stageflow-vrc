namespace StageFlow.App;
internal static class Theme
{
    public static Color Background { get; private set; } = Color.FromArgb(23, 29, 37);
    public static Color Panel { get; private set; } = Color.FromArgb(35, 44, 56);
    public static Color Input { get; private set; } = Color.FromArgb(46, 58, 73);
    public static Color Text { get; private set; } = Color.FromArgb(230, 237, 248);
    public static Color Muted { get; private set; } = Color.FromArgb(151, 165, 185);
    public static Color Accent { get; private set; } = Color.FromArgb(142, 166, 255);
    public static Color Cyan { get; private set; } = Color.FromArgb(110, 211, 228);
    public static Color Selection { get; private set; } = Color.FromArgb(79, 91, 137);
    public static Color Warning { get; private set; } = Color.FromArgb(255, 178, 132);
    public static Color Border { get; private set; } = Color.FromArgb(53, 66, 83);
    private enum Role { Primary, Muted, Normal }
    public static void Configure(AppPreferences preferences)
    {
        (Background, Panel, Input, Text, Muted, Border) = preferences.Skin switch
        {
            AppSkin.Graphite => (Color.FromArgb(24,24,27), Color.FromArgb(34,34,38), Color.FromArgb(47,47,53), Color.FromArgb(235,235,240), Color.FromArgb(162,162,174), Color.FromArgb(60,60,68)),
            AppSkin.Midnight => (Color.FromArgb(10,16,28), Color.FromArgb(19,29,45), Color.FromArgb(28,41,59), Color.FromArgb(228,238,255), Color.FromArgb(141,162,190), Color.FromArgb(41,58,80)),
            AppSkin.Light => (Color.FromArgb(231,235,242), Color.FromArgb(249,250,253), Color.FromArgb(222,228,238), Color.FromArgb(25,35,53), Color.FromArgb(78,91,113), Color.FromArgb(193,203,221)),
            _ => (Color.FromArgb(23,29,37), Color.FromArgb(35,44,56), Color.FromArgb(46,58,73), Color.FromArgb(230,237,248), Color.FromArgb(151,165,185), Color.FromArgb(53,66,83))
        };
        Accent = preferences.Accent switch { AppAccent.Cyan => Color.FromArgb(110,211,228), AppAccent.Violet => Color.FromArgb(192,155,250), AppAccent.Mint => Color.FromArgb(126,218,180), AppAccent.Amber => Color.FromArgb(245,195,117), _ => Color.FromArgb(142,166,255) };
        Selection = Blend(Input, Accent, .35f);
        Cyan = preferences.Skin == AppSkin.Light ? Color.FromArgb(0,113,138) : Color.FromArgb(110,211,228);
        Warning = preferences.Skin == AppSkin.Light ? Color.FromArgb(171,72,27) : Color.FromArgb(255,178,132);
    }
    private static Color Blend(Color a, Color b, float amount) => Color.FromArgb((int)(a.R*(1-amount)+b.R*amount),(int)(a.G*(1-amount)+b.G*amount),(int)(a.B*(1-amount)+b.B*amount));
    public static Button Button(string text, EventHandler? click = null, bool primary = false)
    {
        var button = new ModernButton { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(34, 34), Height = 34, Padding = new Padding(10, 4, 10, 4),
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new Padding(3), Font = new Font("Segoe UI", 9) };
        SetPrimary(button, primary); if (click != null) button.Click += click; return button;
    }
    public static void SetPrimary(Button button, bool primary) { button.Tag = primary ? Role.Primary : Role.Normal; StyleButton(button); }
    private static void StyleButton(Button button)
    {
        bool primary = button.Tag is Role.Primary;
        button.BackColor = primary ? Accent : Input; button.ForeColor = primary ? Color.FromArgb(18,27,42) : Text;
        button.FlatAppearance.BorderColor = Border; button.FlatAppearance.MouseOverBackColor = primary ? Blend(Accent, Color.White,.15f) : Selection;
        button.FlatAppearance.MouseDownBackColor = Selection;
    }
    public static Label Label(string text, bool muted = false) => new() { Text = text, AutoSize = true, ForeColor = muted ? Muted : Text, Tag = muted ? Role.Muted : Role.Normal,
        BackColor = Color.Transparent, Margin = new Padding(3, 8, 3, 4) };
    public static TextBox TextBox(string text = "") => new() { Text = text, BackColor = Input, ForeColor = Text, BorderStyle = BorderStyle.FixedSingle, Width = 160 };
    public static void Apply(Control control, int textSize = 9)
    {
        if (control is AspectPreviewHost) return; // Never recolor the GPU preview or its letterbox.
        control.BackColor = control is Form or SplitContainer ? Background : Panel; control.ForeColor = Text;
        if (control.Font.Size < 16 && control.Font.Size != textSize) control.Font = new Font(control.Font.FontFamily, textSize, control.Font.Style);
        switch (control)
        {
            case Button button: StyleButton(button); break;
            case Label label: label.BackColor = Color.Transparent; label.ForeColor = label.Tag is Role.Muted ? Muted : Text; break;
            case TextBoxBase or ComboBox or NumericUpDown: control.BackColor = Input; break;
            case PropertyGrid grid:
                grid.ViewBackColor = Panel; grid.ViewForeColor = Text; grid.LineColor = Border; grid.CategoryForeColor = Accent;
                grid.CategoryForeColor = Text; grid.CategorySplitterColor = Border; grid.HelpBackColor = Panel; grid.HelpForeColor = Muted; grid.HelpBorderColor = Border;
                grid.SelectedItemWithFocusBackColor = Selection; grid.SelectedItemWithFocusForeColor = Text;
                grid.DisabledItemForeColor = Muted; grid.CommandsBackColor = Panel; grid.CommandsForeColor = Text; grid.Refresh(); return;
            case DataGridView grid:
                grid.BackgroundColor = Panel; grid.GridColor = Border;
                grid.DefaultCellStyle.BackColor = Panel; grid.DefaultCellStyle.ForeColor = Text; grid.DefaultCellStyle.SelectionBackColor = Selection; grid.DefaultCellStyle.SelectionForeColor = Text;
                grid.ColumnHeadersDefaultCellStyle.BackColor = Input; grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted; grid.EnableHeadersVisualStyles = false; return;
            case ToolStrip strip:
                strip.Renderer = new DarkMenuRenderer(); strip.BackColor = Background;
                foreach (ToolStripItem item in strip.Items) StyleMenuItem(item);
                return;
        }
        if (control.ContextMenuStrip is { } menu) Apply(menu, textSize);
        foreach (Control child in control.Controls) Apply(child, textSize);
        control.Invalidate();
    }
    private static void StyleMenuItem(ToolStripItem item)
    {
        item.ForeColor = Text; item.BackColor = Background;
        if (item is ToolStripDropDownItem dropdown)
        {
            dropdown.DropDown.Renderer = new DarkMenuRenderer(); dropdown.DropDown.BackColor = Background;
            foreach (ToolStripItem child in dropdown.DropDownItems) StyleMenuItem(child);
        }
    }
}
internal sealed class SurfacePanel : Panel
{
    public SurfacePanel() { DoubleBuffered = true; BackColor = Theme.Panel; Padding = new Padding(12); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
        using var shape = ModernButton.Rounded(new RectangleF(.5f, .5f, Math.Max(1, Width-1), Math.Max(1, Height-1)), 10);
        using var fill = new SolidBrush(Theme.Panel); e.Graphics.FillPath(fill, shape);
        using var pen = new Pen(Theme.Border); e.Graphics.DrawPath(pen, shape);
    }
}
internal sealed class ModernButton : Button
{
    private bool _hover, _pressed;
    public ModernButton() { DoubleBuffered = true; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Panel); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var shape = Rounded(new RectangleF(.5f, .5f, Math.Max(1, Width-1), Math.Max(1, Height-1)), 6);
        using var fill = new SolidBrush(_pressed ? FlatAppearance.MouseDownBackColor : _hover ? FlatAppearance.MouseOverBackColor : BackColor);
        e.Graphics.FillPath(fill, shape); using var pen = new Pen(Focused ? Theme.Accent : FlatAppearance.BorderColor); e.Graphics.DrawPath(pen, shape);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
    internal static System.Drawing.Drawing2D.GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath(); float d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.Left,bounds.Top,d,d,180,90); path.AddArc(bounds.Right-d,bounds.Top,d,d,270,90);
        path.AddArc(bounds.Right-d,bounds.Bottom-d,d,d,0,90); path.AddArc(bounds.Left,bounds.Bottom-d,d,d,90,90); path.CloseFigure(); return path;
    }
}
internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() { RoundedEdges = false; }
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(Theme.Background);
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { using var brush = new SolidBrush(Theme.Background); e.Graphics.FillRectangle(brush, e.AffectedBounds); }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { using var pen = new Pen(Theme.Border); e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width-1, e.ToolStrip.Height-1); }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    { if (e.Item.Selected || e.Item.Pressed) { using var brush = new SolidBrush(Theme.Selection); e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size)); } }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Muted; base.OnRenderItemText(e); }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Theme.Muted; base.OnRenderArrow(e); }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) { using var pen = new Pen(Theme.Border); e.Graphics.DrawLine(pen, 8, e.Item.Height/2, e.Item.Width-8, e.Item.Height/2); }
}
