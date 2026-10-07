namespace StageFlow.App;

// Native TabControl retains Windows-colored borders. Workspace tabs use one shared palette.
internal sealed class WorkspaceTabs : UserControl
{
    private readonly ActionFlowPanel _header = new() { Dock = DockStyle.Top, Height = 42 };
    private readonly Panel _body = new() { Dock = DockStyle.Fill };
    private readonly List<Panel> _pages = [];
    private int _selected = -1;
    public PageCollection TabPages { get; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex { get => _selected; set { if (value >= 0 && value < _pages.Count) SelectPage(value); } }
    public WorkspaceTabs()
    {
        DoubleBuffered = true; Dock = DockStyle.Fill; TabPages = new(this);
        _header.RequiredHeightChanged += height => _header.Height = height;
        Controls.Add(_body); Controls.Add(_header);
    }
    public sealed class PageCollection(WorkspaceTabs owner)
    {
        public int Count => owner._pages.Count;
        public void Add(Panel page)
        {
            int index = owner._pages.Count; owner._pages.Add(page);
            var button = Theme.Button(page.Text, (_, _) => owner.SelectPage(index));
            button.Padding = new Padding(8, 3, 8, 3); owner._header.Controls.Add(button);
            page.Dock = DockStyle.Fill; page.Visible = false; owner._body.Controls.Add(page);
            if (owner._selected < 0) owner.SelectPage(0);
        }
    }
    private void SelectPage(int index)
    {
        _selected = index;
        for (int i = 0; i < _pages.Count; i++) { _pages[i].Visible = i == index; Theme.SetPrimary((Button)_header.Controls[i], i == index); }
        _pages[index].BringToFront();
    }
}
// Flow rows report their actual wrapped height so large text and narrow panes do
// not hide actions below a fixed-height container.
internal sealed class ActionFlowPanel : FlowLayoutPanel
{
    private bool _layoutQueued;
    public event Action<int>? RequiredHeightChanged;
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        // A TableLayoutPanel ignores a row-size change made inside its own layout.
        // Report after that pass and let the next pass settle the wrapped rows.
        if (_layoutQueued || !IsHandleCreated || IsDisposed) return;
        _layoutQueued = true;
        BeginInvoke(() =>
        {
            _layoutQueued = false; if (IsDisposed) return;
            int bottom = Controls.Cast<Control>().Where(c => c.Visible).Select(c => c.Bottom + c.Margin.Bottom).DefaultIfEmpty(Padding.Top).Max();
            RequiredHeightChanged?.Invoke(bottom + Padding.Bottom);
        });
    }
}
internal sealed class AspectPreviewHost : Panel
{
    private readonly Control _surface;
    private Size _canvas = new(1920, 1080);
    public AspectPreviewHost(Control surface)
    {
        DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Fill; BackColor = Theme.Background; Margin = Padding.Empty; Padding = new Padding(12);
        _surface = surface; surface.Dock = DockStyle.None; surface.Margin = Padding.Empty; surface.BackColor = Color.Black; Controls.Add(surface);
    }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Size CanvasSize { get => _canvas; set { if (_canvas == value) return; _canvas = value; PerformLayout(); } }
    internal static Rectangle Fit(Size available, Size canvas)
    {
        if (available.Width <= 0 || available.Height <= 0 || canvas.Width <= 0 || canvas.Height <= 0) return Rectangle.Empty;
        double scale = Math.Min((double)available.Width / canvas.Width, (double)available.Height / canvas.Height);
        int width = Math.Max(1, (int)Math.Floor(canvas.Width * scale)), height = Math.Max(1, (int)Math.Floor(canvas.Height * scale));
        return new((available.Width - width) / 2, (available.Height - height) / 2, width, height);
    }
    [System.ComponentModel.Browsable(false)]
    public Rectangle ProgramBounds
    {
        get { var bounds = Fit(new Size(Math.Max(0, ClientSize.Width - Padding.Horizontal), Math.Max(0, ClientSize.Height - Padding.Vertical)), _canvas); bounds.Offset(Padding.Left, Padding.Top); return bounds; }
    }
    protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); if (_surface != null) _surface.Bounds = ProgramBounds; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var bounds = ProgramBounds;
        if (bounds.Width == 0 || bounds.Height == 0) return;
        bounds.Inflate(1, 1); using var pen = new Pen(Theme.Muted); e.Graphics.DrawRectangle(pen, bounds);
    }
}

internal sealed class ThemedComboBox : ComboBox
{
    public ThemedComboBox() { DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = 24; FlatStyle = FlatStyle.Flat; }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        using var brush = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Theme.Selection : Theme.Input);
        e.Graphics.FillRectangle(brush, e.Bounds);
        string text = e.Index >= 0 && e.Index < Items.Count ? GetItemText(Items[e.Index]) ?? "" : Text;
        TextRenderer.DrawText(e.Graphics, text, Font, new Rectangle(e.Bounds.X+6,e.Bounds.Y,e.Bounds.Width-8,e.Bounds.Height), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class TimelineSlider : Control
{
    private int _value;
    private bool _dragging;
    private double[] _markers = [];
    public TimelineSlider() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true); TabStop = true; Cursor = Cursors.Hand; }
    [System.ComponentModel.DefaultValue(0)] public int Minimum { get; set; }
    [System.ComponentModel.DefaultValue(1000000)] public int Maximum { get; set; } = 1000000;
    [System.ComponentModel.DefaultValue(0)] public int Value { get => _value; set { int next = Math.Clamp(value, Minimum, Math.Max(Minimum,Maximum)); if (next != _value) { _value = next; Invalidate(); } } }
    public event EventHandler? Scroll;
    public void SetMarkers(IEnumerable<double> markers) { _markers = markers.Where(x => double.IsFinite(x) && x >= 0 && x <= 1).ToArray(); Invalidate(); }
    private void Pointer(int x) { Value = Minimum + (int)(Math.Clamp((x-10.0)/Math.Max(1,Width-20),0,1) * (Maximum-Minimum)); Scroll?.Invoke(this,EventArgs.Empty); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { _dragging = Capture = true; Focus(); Pointer(e.X); } }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (_dragging) Pointer(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { if (_dragging) Pointer(e.X); _dragging = false; Capture = false; } base.OnMouseUp(e); }
    protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) _dragging = false; base.OnMouseCaptureChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Panel); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float y = Height/2f, end = Math.Max(10, Width-10), position = 10 + (end-10) * (Value-Minimum) / Math.Max(1,Maximum-Minimum);
        using var rail = new Pen(Theme.Border,4); using var played = new Pen(Enabled ? Theme.Cyan : Theme.Muted,4);
        e.Graphics.DrawLine(rail,10,y,end,y); e.Graphics.DrawLine(played,10,y,position,y);
        using var markerPen = new Pen(Theme.Accent,2); foreach (double marker in _markers) { float x = 10+(end-10)*(float)marker; e.Graphics.DrawLine(markerPen,x,y-8,x,y+8); }
        using var thumb = new SolidBrush(Enabled ? Theme.Cyan : Theme.Muted); e.Graphics.FillEllipse(thumb,position-6,y-6,12,12);
    }
}
