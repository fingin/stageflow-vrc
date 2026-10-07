namespace StageFlow.Core.ArtNet;

/// <summary>Bounded universe state. Snapshot copies only channel bytes, never rendered pixels.</summary>
public sealed class DmxUniverseStore
{
    private sealed class Universe
    {
        public readonly byte[] Channels = new byte[512];
        public readonly byte[] Output = new byte[512];
        public byte Sequence;
        public uint Sender;
        public long LastArrival;
        public long Revision;
    }
    private readonly object _gate = new();
    private readonly Universe[] _universes;
    public int FirstUniverse { get; }
    public int Count => _universes.Length;
    private long _revision, _accepted, _outOfOrder;
    private int _activeChannelCount;
    private bool _synchronized, _pending;
    private uint _lastSender;
    private long _lastSync, _syncTimeout;
    public int ActiveChannelCount => Volatile.Read(ref _activeChannelCount);
    public long Accepted => Interlocked.Read(ref _accepted);
    public long OutOfOrder => Interlocked.Read(ref _outOfOrder);
    public long Revision => Interlocked.Read(ref _revision);

    public DmxUniverseStore(int firstUniverse, int count)
    {
        if (firstUniverse < 0 || count is < 1 or > 512 || firstUniverse + count > 32768)
            throw new ArgumentOutOfRangeException(nameof(count));
        FirstUniverse = firstUniverse;
        _universes = Enumerable.Range(0, count).Select(_ => new Universe()).ToArray();
    }

    public bool Apply(in ArtDmx packet, uint sender, long arrivalTicks, long sequenceResetTicks)
    {
        int index = packet.Universe - FirstUniverse;
        if ((uint)index >= _universes.Length) return false;
        lock (_gate)
        {
            ExpireSync(arrivalTicks);
            Universe u = _universes[index];
            if (u.Revision > 0 && u.Sender == sender && packet.Sequence != 0 && u.Sequence != 0 &&
                arrivalTicks - u.LastArrival < sequenceResetTicks)
            {
                int delta = (packet.Sequence - u.Sequence + 256) & 255;
                if (delta == 0 || delta > 127) { Interlocked.Increment(ref _outOfOrder); return false; }
            }
            packet.Channels.CopyTo(u.Channels);
            // Short ArtDmx packets have no values for the remaining channels.
            u.Channels.AsSpan(packet.Channels.Length).Clear();
            u.Sequence = packet.Sequence; u.Sender = sender; u.LastArrival = arrivalTicks;
            _lastSender = sender;
            u.Revision++;
            if (_synchronized) _pending = true;
            else { u.Channels.CopyTo(u.Output, 0); _revision++; }
            _activeChannelCount = Math.Max(_activeChannelCount, index * 512 + packet.Channels.Length);
            Interlocked.Increment(ref _accepted);
            return true;
        }
    }

    public long CopyTo(Span<byte> destination, long? nowTicks = null)
    {
        if (destination.Length < Count * 512) throw new ArgumentException("DMX snapshot buffer too small.");
        lock (_gate)
        {
            ExpireSync(nowTicks ?? System.Diagnostics.Stopwatch.GetTimestamp());
            for (int i = 0; i < Count; i++) _universes[i].Output.CopyTo(destination.Slice(i * 512, 512));
            return _revision;
        }
    }
    public void Synchronize(uint sender, long nowTicks, long timeoutTicks)
    {
        lock (_gate)
        {
            if (_accepted == 0 || sender != _lastSender) return;
            CommitPending(); _synchronized = true; _lastSync = nowTicks; _syncTimeout = timeoutTicks;
        }
    }
    private void ExpireSync(long now)
    { if (_synchronized && now - _lastSync >= _syncTimeout) { CommitPending(); _synchronized = false; } }
    private void CommitPending()
    {
        if (!_pending) return;
        foreach (var u in _universes) u.Channels.CopyTo(u.Output, 0);
        _pending = false; _revision++;
    }
    public void Blackout()
    {
        lock (_gate)
        {
            foreach (var universe in _universes) { universe.Channels.AsSpan().Clear(); universe.Output.AsSpan().Clear(); universe.Sequence = 0; }
            _pending = false;
            _revision++;
        }
    }
}
