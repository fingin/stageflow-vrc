using System.Net;
using System.Net.NetworkInformation;
using StageFlow.Core.Monitoring;
using StageFlow.Core.Projects;

namespace StageFlow.App;

public sealed partial class MainWindow
{
    private readonly DataGridView _monitorRoutes = new() { Name = "MonitorRoutes", Dock = DockStyle.Fill, ReadOnly = true,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
        MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BorderStyle = BorderStyle.None, ClipboardCopyMode = DataGridViewClipboardCopyMode.Disable };
    private readonly RichTextBox _monitorDetail = new() { Name = "MonitorDetail", Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None,
        DetectUrls = true, ScrollBars = RichTextBoxScrollBars.Vertical, WordWrap = true };
    private readonly ComboBox _monitorLinks = new ThemedComboBox { Name = "MonitorLinks", Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Label" };
    private readonly TextBox _monitorAddress = new() { Name = "MonitorAddress", AccessibleName = "Selected stream address", Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.FixedSingle };
    private readonly Button _monitorOpen = Theme.Button("Open link"), _monitorCopy = Theme.Button("Copy address");
    private IReadOnlyList<MonitorRoute> _monitorRows = [];
    private IPAddress[] _monitorInterfaces = [];
    private long _monitorLastUi;
    private bool _monitorBinding, _monitorDiscovering;
    private Control? _monitorBody;
    private string? _monitorLinkError;

    private Control BuildMonitorBody(Control performanceActions)
    {
        var body = new Panel { Dock = DockStyle.Fill }; _monitorBody = body;
        var view = new ThemedComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        view.Items.AddRange(["Connections & encoders", "Performance"]); view.SelectedIndex = 0;
        var scroll = new Panel { Dock = DockStyle.Fill };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); table.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 80)); table.RowStyles.Add(new RowStyle(SizeType.Percent, 45)); table.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        _monitorRoutes.Columns.Add(new DataGridViewTextBoxColumn { Name = "Route", HeaderText = "Route / encoder", FillWeight = 72 });
        _monitorRoutes.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "Status", FillWeight = 28 });
        _monitorRoutes.CurrentCellChanged += (_, _) => { if (!_monitorBinding) UpdateMonitorDetail(force: true); };
        _monitorLinks.SelectedIndexChanged += (_, _) => UpdateMonitorLinkButtons();
        _monitorOpen.Click += (_, _) => OpenMonitorLink();
        _monitorCopy.Click += (_, _) =>
        {
            if (_monitorLinks.SelectedItem is MonitorLink link)
                try { Clipboard.SetText(link.Url); _status.Text = "Copied " + link.Label; } catch (Exception ex) { _status.Text = "Clipboard: " + ex.Message; }
        };
        _monitorDetail.LinkClicked += (_, e) =>
        {
            var link = SelectedMonitorRoute()?.Links.FirstOrDefault(l => l.OpenInBrowser && l.Url == e.LinkText);
            if (link != null) TryOpenMonitorLink(link);
        };
        var actions = new ActionFlowPanel { Dock = DockStyle.Fill };
        actions.RequiredHeightChanged += height => table.RowStyles[2].Height = height + 6;
        actions.Controls.Add(_monitorOpen); actions.Controls.Add(_monitorCopy);
        actions.Controls.Add(Theme.Button("Settings…", (_, _) =>
        {
            var row = SelectedMonitorRoute(); if (row == null) return;
            if (row.Settings == "Selection" && Guid.TryParse(row.Id, out var layer)) { _selected = layer; RefreshProjectUi(); Inspect("Selection"); }
            else ShowPreferences(row.Settings);
        }));
        actions.Controls.Add(Theme.Button("Refresh links", async (_, _) => await DiscoverMonitorLinks()));
        table.Controls.Add(_monitorLinks, 0, 0); table.Controls.Add(_monitorAddress, 0, 1); table.Controls.Add(actions, 0, 2); table.Controls.Add(_monitorRoutes, 0, 3); table.Controls.Add(_monitorDetail, 0, 4);
        var performance = new Panel { Dock = DockStyle.Fill, Visible = false };
        performance.Controls.Add(_diagnostics); performance.Controls.Add(performanceActions); performance.Controls.Add(_recordLabel);
        scroll.Controls.Add(table); body.Controls.Add(scroll); body.Controls.Add(performance); body.Controls.Add(view);
        view.SelectedIndexChanged += (_, _) => { scroll.Visible = view.SelectedIndex == 0; performance.Visible = view.SelectedIndex == 1; RefreshMonitor(force: true); };
        body.VisibleChanged += (_, _) => { if (body.Visible) RefreshMonitor(force: true); };
        Shown += async (_, _) => await DiscoverMonitorLinks();
        return body;
    }
    private async Task DiscoverMonitorLinks()
    {
        if (_monitorDiscovering) return; _monitorDiscovering = true;
        try
        {
            var addresses = await Task.Run(() => NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address).Distinct().ToArray());
            if (IsDisposed) return; _monitorInterfaces = addresses; _monitorLinkError = null;
        }
        catch (Exception ex) { _monitorLinkError = "LAN address discovery: " + ex.Message; }
        finally { _monitorDiscovering = false; if (!IsDisposed) RefreshMonitor(force: true); }
    }
    private void RefreshMonitor(bool force = false)
    {
        if (_monitorBody == null || !force && (!_monitorBody.Visible || Environment.TickCount64 - _monitorLastUi < 500)) return;
        RenderMonitor(_engine?.Project ?? WorkingProject, _engine?.Status.Video);
    }
    internal void RenderMonitor(ShowProject project, VideoTelemetry? telemetry)
    {
        _monitorLastUi = Environment.TickCount64;
        string? selected = SelectedMonitorRoute()?.Id;
        var rows = VideoMonitor.Build(project, telemetry, _monitorInterfaces).ToList();
        if (_engine?.MediaInfo is { } media)
        {
            var error = _engine.Status.MediaError ?? telemetry?.FatalError;
            rows.Add(new("movie", "Movie · " + Path.GetFileName(media.Path), error != null ? VideoRouteState.Error :
                telemetry?.Playing == true ? VideoRouteState.Live : VideoRouteState.Idle,
                $"{media.VideoCodec} · {media.Width} × {media.Height} · {media.VideoFps:0.###} fps\nAudio: {media.AudioCodec ?? "none"} · {media.AudioSampleRate} Hz · {media.AudioChannels} channels\n" +
                "Playback follows the master transport.\n" + VideoMonitor.SafeError(error, project), [], "Video & canvas"));
        }
        _monitorRows = rows;
        _monitorBinding = true;
        try
        {
            bool rebuild = !_monitorRoutes.Rows.Cast<DataGridViewRow>().Select(r => r.Tag as string).SequenceEqual(_monitorRows.Select(r => r.Id));
            if (rebuild) { _monitorRoutes.Rows.Clear(); foreach (var row in _monitorRows) { int index = _monitorRoutes.Rows.Add(row.Name, VideoMonitor.StateText(row.State)); _monitorRoutes.Rows[index].Tag = row.Id; } }
            for (int i = 0; i < _monitorRows.Count; i++)
            {
                var model = _monitorRows[i]; var row = _monitorRoutes.Rows[i];
                if (!Equals(row.Cells[0].Value, model.Name)) row.Cells[0].Value = model.Name;
                string state = VideoMonitor.StateText(model.State); if (!Equals(row.Cells[1].Value, state)) row.Cells[1].Value = state;
                row.Cells[0].ToolTipText = model.Name; row.Cells[1].ToolTipText = model.Detail;
                row.Cells[1].Style.ForeColor = model.State is VideoRouteState.Error or VideoRouteState.Stalled ? Theme.Warning : model.State == VideoRouteState.Live ? Theme.Cyan : Theme.Muted;
                if (rebuild && model.Id == selected) _monitorRoutes.CurrentCell = row.Cells[0];
            }
            if (_monitorRoutes.CurrentCell == null && _monitorRoutes.Rows.Count > 0) _monitorRoutes.CurrentCell = _monitorRoutes.Rows[0].Cells[0];
        }
        finally { _monitorBinding = false; }
        UpdateMonitorDetail();
    }
    private MonitorRoute? SelectedMonitorRoute() => _monitorRoutes.CurrentRow?.Tag is string id ? _monitorRows.FirstOrDefault(r => r.Id == id) : null;
    private void UpdateMonitorDetail(bool force = false)
    {
        var row = SelectedMonitorRoute();
        string text = row == null ? "Select a route to inspect its status and links." : row.Name + " · " + VideoMonitor.StateText(row.State) + "\n\n" + row.Detail +
            "\n\n" + string.Join("\n\n", row.Links.Select(l => l.Label + "\n" + l.Url));
        if (_monitorLinkError != null && row?.Id == "hls") text += "\n\n" + _monitorLinkError;
        // Avoid moving the user's selection while they copy status text.
        if ((force || !_monitorDetail.Focused) && _monitorDetail.Text != text) _monitorDetail.Text = text;
        var links = row?.Links ?? [];
        if (!_monitorLinks.Items.Cast<MonitorLink>().SequenceEqual(links))
        {
            string? previous = (_monitorLinks.SelectedItem as MonitorLink)?.Url;
            _monitorLinks.Items.Clear(); foreach (var link in links) _monitorLinks.Items.Add(link);
            if (_monitorLinks.Items.Count > 0) _monitorLinks.SelectedIndex = Math.Max(0, links.ToList().FindIndex(l => l.Url == previous));
        }
        UpdateMonitorLinkButtons();
    }
    private void UpdateMonitorLinkButtons()
    {
        var link = _monitorLinks.SelectedItem as MonitorLink;
        if (_monitorAddress.Text != (link?.Url ?? "")) _monitorAddress.Text = link?.Url ?? "";
        _monitorCopy.Enabled = link != null;
        _monitorOpen.Enabled = link?.OpenInBrowser == true && SelectedMonitorRoute()?.State == VideoRouteState.Live;
    }
    private void OpenMonitorLink() { if (_monitorLinks.SelectedItem is MonitorLink link && _monitorOpen.Enabled) TryOpenMonitorLink(link); }
    private void TryOpenMonitorLink(MonitorLink link)
    {
        if (!link.OpenInBrowser || SelectedMonitorRoute()?.State != VideoRouteState.Live || !Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return;
        try { OpenExternal(link.Url); } catch (Exception ex) { _status.Text = "Open link: " + ex.Message; }
    }
}
