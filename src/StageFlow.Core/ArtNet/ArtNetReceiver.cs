using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace StageFlow.Core.ArtNet;

public sealed class ArtNetReceiver : IAsyncDisposable
{
    private readonly DmxUniverseStore _store;
    private readonly Socket _socket;
    private readonly CancellationTokenSource _cancel = new();
    private Task? _worker;
    private long _invalid;
    private string? _lastError;
    public long InvalidPackets => Interlocked.Read(ref _invalid);
    public string? LastError => Volatile.Read(ref _lastError);
    public int Port => ((IPEndPoint)_socket.LocalEndPoint!).Port;

    public ArtNetReceiver(DmxUniverseStore store, IPAddress address, int port = ArtNetPacket.DefaultPort)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) throw new ArgumentException("Art-Net requires IPv4.");
        _store = store;
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        { ReceiveBufferSize = 1024 * 1024, ExclusiveAddressUse = true };
        try { _socket.Bind(new IPEndPoint(address, port)); }
        catch { _socket.Dispose(); _cancel.Dispose(); throw; }
    }
    public void Start()
    {
        if (_worker != null) throw new InvalidOperationException("Receiver is already running.");
        _worker = Task.Run(ReceiveLoop);
    }
    private async Task ReceiveLoop()
    {
        byte[] buffer = new byte[2048];
        EndPoint any = new IPEndPoint(IPAddress.Any, 0);
        try
        {
            while (!_cancel.IsCancellationRequested)
            {
                var result = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, any, _cancel.Token).ConfigureAwait(false);
                ProcessPacket(buffer.AsSpan(0, result.ReceivedBytes), (IPEndPoint)result.RemoteEndPoint);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) when (_cancel.IsCancellationRequested) { }
        catch (Exception ex) { Volatile.Write(ref _lastError, ex.Message); }
    }
    private void ProcessPacket(ReadOnlySpan<byte> bytes, IPEndPoint sender)
    {
        if (ArtNetPacket.TryReadDmx(bytes, out var packet))
        {
            Span<byte> address = stackalloc byte[4];
            sender.Address.TryWriteBytes(address, out _);
            _store.Apply(packet, BinaryPrimitives.ReadUInt32BigEndian(address), Stopwatch.GetTimestamp(), Stopwatch.Frequency * 3);
        }
        else if (!ArtNetPacket.TryGetOpcode(bytes, out _)) Interlocked.Increment(ref _invalid);
    }
    public async ValueTask DisposeAsync()
    {
        await _cancel.CancelAsync().ConfigureAwait(false);
        _socket.Dispose();
        if (_worker != null) await _worker.ConfigureAwait(false);
        _cancel.Dispose();
    }
}
