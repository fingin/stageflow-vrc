using StageFlow.Core.Projects;

namespace StageFlow.App;

public sealed partial class MainWindow
{
    private Form? _networkWindow;
    private Action? _refreshNetwork;
    private Label? _networkFeedback;
    internal LiveProperties BuildNetworkProperties(Guid id)
    {
        var bag = new LiveProperties("Destination");
        NetworkStreamOutput Get() => WorkingProject.NetworkOutputs.First(o => o.Id == id);
        void Commit(ShowProject project, NetworkStreamOutput value) => QueueLive("Edit destination",
            project with { NetworkOutputs = project.NetworkOutputs.Select(o => o.Id == id ? value : o).ToList() });
        AddRecord(bag, "01 · Destination", Get, Commit, include: ["Name", "Enabled", "Url"]);
        bag.Add("Protocol", "01 · Destination", typeof(NetworkStreamProtocol), () => Get().Protocol, null);
        if (Get().Protocol == NetworkStreamProtocol.Rtmp)
        {
            AddRecord(bag, "02 · Publishing", Get, Commit, include: ["StreamKey"]);
            AddRecord(bag, "03 · Authentication", Get, Commit, include: ["AuthenticationEnabled", "Username", "Password"]);
        }
        bag.Add("Encoding", "04 · Shared program", typeof(string), () => WorkingProject.Stream.Encoder + " · " + WorkingProject.Stream.RateControl + " · " + WorkingProject.Stream.VideoBitrateKbps + " kbps", null,
            "All encoded destinations share Preferences → Encoding. This avoids duplicate encodes.");
        return bag;
    }
    internal void ShowNetworkOutputs()
    {
        if (_networkWindow is { IsDisposed: false }) { _networkWindow.Activate(); return; }
        var dialog = new Form { Text = "StageFlow v2 · Network destinations", Size = new Size(900, 700), MinimumSize = new Size(760, 580),
            StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Background, ShowInTaskbar = false };
        _networkWindow = dialog;
        var list = new ListBox { Name = "NetworkDestinationList", Dock = DockStyle.Left, Width = 230, BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, DisplayMember = "Name" };
        var properties = CreatePropertyGrid(); properties.Name = "NetworkDestinationProperties";
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, AutoScroll = true };
        var note = Theme.Label("Changes update automatically. Active streams reconnect after edits. Keys and passwords are saved with the show.", true);
        note.Dock = DockStyle.Bottom; note.Height = 54; note.AutoSize = false; note.Padding = new Padding(10);
        _networkFeedback = Theme.Label("Changes update automatically", true); _networkFeedback.Dock = DockStyle.Bottom; _networkFeedback.Height = 30; _networkFeedback.AutoSize = false;
        void RefreshList(Guid? select = null)
        {
            properties.SelectedObject = null; list.Items.Clear();
            foreach (var output in WorkingProject.NetworkOutputs) list.Items.Add(output);
            for (int i = 0; i < list.Items.Count; i++) if (((NetworkStreamOutput)list.Items[i]).Id == select) list.SelectedIndex = i;
        }
        foreach (var protocol in Enum.GetValues<NetworkStreamProtocol>()) buttons.Controls.Add(Theme.Button("+ " + protocol, (_, _) =>
        {
            try
            {
                var output = new NetworkStreamOutput { Name = protocol + " " + (WorkingProject.NetworkOutputs.Count + 1), Protocol = protocol,
                    Url = protocol == NetworkStreamProtocol.Rtmp ? "rtmp://127.0.0.1/live" : "srt://127.0.0.1:9001?mode=caller&latency=120000",
                    StreamKey = protocol == NetworkStreamProtocol.Rtmp ? "stageflow" : "" };
                QueueLive("Add destination", WorkingProject with { NetworkOutputs = [.. WorkingProject.NetworkOutputs, output] }); RefreshList(output.Id);
            }
            catch (Exception ex) { ShowError(ex); }
        }));
        buttons.Controls.Add(Theme.Button("Remove", (_, _) =>
        {
            if (list.SelectedItem is not NetworkStreamOutput output) return;
            try { QueueLive("Remove destination", WorkingProject with { NetworkOutputs = WorkingProject.NetworkOutputs.Where(o => o.Id != output.Id).ToList() }); RefreshList(); }
            catch (Exception ex) { ShowError(ex); }
        }));
        buttons.Controls.Add(Theme.Button("Encoding settings…", (_, _) => ShowPreferences("Encoding")));
        list.SelectedIndexChanged += (_, _) => properties.SelectedObject = list.SelectedItem is NetworkStreamOutput output ? BuildNetworkProperties(output.Id) : null;
        properties.PropertyValueChanged += (_, _) => RefreshList((list.SelectedItem as NetworkStreamOutput)?.Id);
        _refreshNetwork = () =>
        {
            if (!list.Items.Cast<NetworkStreamOutput>().Select(o => o.Id).SequenceEqual(WorkingProject.NetworkOutputs.Select(o => o.Id)) || !properties.ContainsFocus)
                RefreshList((list.SelectedItem as NetworkStreamOutput)?.Id);
        };
        dialog.Controls.Add(properties); dialog.Controls.Add(list); dialog.Controls.Add(note); dialog.Controls.Add(_networkFeedback); dialog.Controls.Add(buttons);
        dialog.FormClosed += (_, _) => { _networkWindow = null; _networkFeedback = null; _refreshNetwork = null; };
        RefreshList(WorkingProject.NetworkOutputs.FirstOrDefault()?.Id); Theme.Apply(dialog, _preferences.TextSize); dialog.Show(this);
    }
}
