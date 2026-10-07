namespace StageFlow.Core.Projects;
public enum NetworkStreamProtocol { Rtmp, Srt }
public sealed record NetworkStreamOutput
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Network stream";
    public bool Enabled { get; init; }
    public NetworkStreamProtocol Protocol { get; init; }
    public string Url { get; init; } = "rtmp://127.0.0.1/live/stageflow";
    public void Validate()
    {
        if (!Enum.IsDefined(Protocol) || string.IsNullOrWhiteSpace(Name) || Name.Length > 200 || Url.Length > 4096 ||
            !Uri.TryCreate(Url, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host) ||
            (Protocol == NetworkStreamProtocol.Rtmp ? uri.Scheme is not ("rtmp" or "rtmps") : uri.Scheme != "srt"))
            throw new InvalidDataException("Network outputs require a valid RTMP(S) or SRT URL matching the protocol.");
    }
}
public sealed record LanVideoSettings
{
    public bool NdiEnabled { get; init; }
    public string NdiName { get; init; } = "StageFlow v2 Program";
    public bool OmtEnabled { get; init; }
    public string OmtName { get; init; } = "StageFlow v2 Program";
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(NdiName) || string.IsNullOrWhiteSpace(OmtName) || NdiName.Length > 200 || OmtName.Length > 200)
            throw new InvalidDataException("LAN output names must contain 1–200 characters.");
    }
}
