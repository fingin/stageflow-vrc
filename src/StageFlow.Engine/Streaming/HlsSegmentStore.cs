using System.Globalization;
using System.Text;

namespace StageFlow.Engine.Streaming;

public sealed record HlsSegment(long Sequence, double Duration, byte[] Data, bool Discontinuity);

/// <summary>Immutable published segments shared by all viewers. Bounded retention, atomic playlists.</summary>
public sealed class HlsSegmentStore
{
    private readonly object _gate = new();
    private readonly LinkedList<HlsSegment> _segments = [];
    private readonly int _playlistLength;
    private readonly long _maxBytes;
    private readonly int _targetDuration;
    private long _bytes, _sequence, _discontinuities;
    private bool _ended;
    public HlsSegmentStore(int playlistLength = 6, long maxBytes = 256 * 1024 * 1024, double targetSeconds = 1)
    {
        if (playlistLength is < 3 or > 30 || maxBytes < 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(playlistLength));
        _playlistLength = playlistLength; _maxBytes = maxBytes;
        if (!double.IsFinite(targetSeconds) || targetSeconds is < 0.5 or > 10) throw new ArgumentOutOfRangeException(nameof(targetSeconds));
        _targetDuration = (int)Math.Ceiling(targetSeconds);
    }
    public long Bytes { get { lock (_gate) return _bytes; } }
    public int SegmentCount { get { lock (_gate) return _segments.Count; } }
    public void Publish(byte[] bytes, double duration, bool discontinuity = false)
    {
        if (bytes.Length == 0 || bytes.Length > _maxBytes / 3 || !double.IsFinite(duration) || duration <= 0)
            throw new ArgumentException("Invalid HLS segment.");
        if (Math.Round(duration, MidpointRounding.AwayFromZero) > _targetDuration)
            throw new ArgumentException("Segment exceeds the stream's fixed HLS target duration.");
        lock (_gate)
        {
            if (_ended) throw new InvalidOperationException("Stream has ended.");
            _segments.AddLast(new HlsSegment(_sequence++, duration, bytes, discontinuity)); _bytes += bytes.Length;
            // Retain two extra segments for clients already holding a previous playlist.
            while (_segments.Count > _playlistLength + 2 || _bytes > _maxBytes)
            {
                var first = _segments.First!.Value;
                _bytes -= first.Data.Length;
                if (first.Discontinuity) _discontinuities++;
                _segments.RemoveFirst();
            }
        }
    }
    public HlsSegment? Get(long sequence)
    { lock (_gate) return _segments.FirstOrDefault(s => s.Sequence == sequence); }
    public void End() { lock (_gate) _ended = true; }
    public string? Playlist()
    {
        lock (_gate)
        {
            if (_segments.Count == 0) return null;
            var visible = _segments.Skip(Math.Max(0, _segments.Count - _playlistLength)).ToArray();
            long skippedDiscontinuities = _segments.Take(_segments.Count - visible.Length).Count(s => s.Discontinuity);
            var text = new StringBuilder("#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-INDEPENDENT-SEGMENTS\n");
            text.Append("#EXT-X-TARGETDURATION:").Append(_targetDuration).Append('\n');
            text.Append("#EXT-X-MEDIA-SEQUENCE:").Append(visible[0].Sequence).Append('\n');
            text.Append("#EXT-X-DISCONTINUITY-SEQUENCE:").Append(_discontinuities + skippedDiscontinuities).Append('\n');
            foreach (var segment in visible)
            {
                if (segment.Discontinuity) text.Append("#EXT-X-DISCONTINUITY\n");
                text.Append("#EXTINF:").Append(segment.Duration.ToString("0.000000", CultureInfo.InvariantCulture)).Append(",\n");
                text.Append("segment-").Append(segment.Sequence).Append(".ts\n");
            }
            if (_ended) text.Append("#EXT-X-ENDLIST\n");
            return text.ToString();
        }
    }
}
