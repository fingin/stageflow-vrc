namespace StageFlow.Core.Projects;
public enum NetworkStreamProtocol { Rtmp, Srt }
public sealed record NetworkStreamOutput
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Network stream";
    public bool Enabled { get; init; }
    public NetworkStreamProtocol Protocol { get; init; }
    public string Url { get; init; } = "rtmp://127.0.0.1/live/stageflow";
    public string StreamKey { get; init; } = "";
    public bool AuthenticationEnabled { get; init; }
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public void Validate()
    {
        if (!Enum.IsDefined(Protocol) || string.IsNullOrWhiteSpace(Name) || Name.Length > 200 || string.IsNullOrWhiteSpace(Url) || Url.Length > 4096 || Url.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) ||
            !Uri.TryCreate(Url, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host) ||
            (Protocol == NetworkStreamProtocol.Rtmp ? uri.Scheme is not ("rtmp" or "rtmps") : uri.Scheme != "srt"))
            throw new InvalidDataException("Network outputs require a valid RTMP(S) or SRT URL matching the protocol.");
        if (StreamKey == null || StreamKey.Length > 2048 || StreamKey.Any(char.IsControl) || Username == null || Password == null)
            throw new InvalidDataException("Invalid stream key or authentication fields.");
        if (Protocol == NetworkStreamProtocol.Rtmp)
        {
            if (AuthenticationEnabled && (System.Text.Encoding.UTF8.GetByteCount(Username) > 49 || System.Text.Encoding.UTF8.GetByteCount(Password) > 49 ||
                Username.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || ":@/?#\\&=+%".Contains(c)) || Password.Any(c => char.IsControl(c) || "/?#\\".Contains(c))))
                throw new InvalidDataException("RTMP authentication allows up to 49 UTF-8 bytes per credential; / ? # and backslash are unsupported. Usernames also cannot contain whitespace or : @ & = + %.");
            if (Enabled && AuthenticationEnabled && (string.IsNullOrWhiteSpace(Username) || Password.Length == 0))
                throw new InvalidDataException("Enter the RTMP username and password before enabling this destination.");
            if (StreamKey.Length > 0 && (uri.AbsolutePath.Length + uri.Query.Length > 1000 || uri.Fragment.Length > 0))
                throw new InvalidDataException("RTMP server/application path is too long or contains a fragment.");
        }
    }
    // Native FFmpeg RTMP does not URL-decode credentials. Do not percent-escape them;
    // Validate rejects delimiters it cannot represent rather than silently changing a password.
    public string ConnectionUrl()
    {
        Validate();
        if (Protocol != NetworkStreamProtocol.Rtmp || !AuthenticationEnabled) return Url;
        int authorityStart = Url.IndexOf("://", StringComparison.Ordinal) + 3;
        int end = Url.IndexOfAny(['/', '?', '#'], authorityStart); if (end < 0) end = Url.Length;
        int userInfoEnd = Url.LastIndexOf('@', end - 1, end - authorityStart);
        return Url[..authorityStart] + Username + ":" + Password + "@" + Url[(userInfoEnd >= authorityStart ? userInfoEnd + 1 : authorityStart)..];
    }
    public string? RtmpApplication() => Protocol == NetworkStreamProtocol.Rtmp && StreamKey.Length > 0
        ? new Uri(Url).PathAndQuery.TrimStart('/').TrimEnd('/') : null;
    public override string ToString() => Name;
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
