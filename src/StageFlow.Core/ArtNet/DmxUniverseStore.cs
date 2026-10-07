namespace StageFlow.Core.ArtNet;

/// <summary>Bounded universe state. Snapshot copies only channel bytes, never rendered pixels.</summary>
public sealed class DmxUniverseStore
{
    private sealed class Universe
    {
        public readonly byte[] Channels = new byte[512];
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
            u.Revision = ++_revision;
            _activeChannelCount = Math.Max(_activeChannelCount, index * 512 + packet.Channels.Length);
            Interlocked.Increment(ref _accepted);
            return true;
        }
    }

    public long CopyTo(Span<byte> destination)
    {
        if (destination.Length < Count * 512) throw new ArgumentException("DMX snapshot buffer too small.");
        lock (_gate)
        {
            for (int i = 0; i < Count; i++) _universes[i].Channels.CopyTo(destination.Slice(i * 512, 512));
            return _revision;
        }
    }
    public void Blackout()
    {
        lock (_gate)
        {
            foreach (var universe in _universes) { universe.Channels.AsSpan().Clear(); universe.Sequence = 0; }
            _revision++;
        }
    }
}
