using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace StageFlow.Core.Osc;

public sealed record OscReceived(OscMessage Message, string Sender);
public sealed class OscListener : IDisposable
{
    private readonly UdpClient _udp;
    private readonly CancellationTokenSource _cancel = new();
    private readonly Task _read;
    private readonly Channel<OscReceived> _messages = Channel.CreateBounded<OscReceived>(new BoundedChannelOptions(512) { FullMode = BoundedChannelFullMode.Wait, SingleWriter = true });
    private long _received, _dropped;
    public long Received => Interlocked.Read(ref _received);
    public long Dropped => Interlocked.Read(ref _dropped);
    public string? Error { get; private set; }
    public OscListener(string address, int port)
    { _udp = new UdpClient(new IPEndPoint(IPAddress.Parse(address), port)); _read = Task.Run(Read); }
    public bool TryRead(out OscReceived? message) => _messages.Reader.TryRead(out message);
    private async Task Read()
    {
        try
        {
            while (!_cancel.IsCancellationRequested)
            {
                var packet = await _udp.ReceiveAsync(_cancel.Token).ConfigureAwait(false);
                if (!OscCodec.TryDecode(packet.Buffer, out var messages)) { Interlocked.Increment(ref _dropped); continue; }
                foreach (var message in messages)
                {
                    Interlocked.Increment(ref _received);
                    if (!_messages.Writer.TryWrite(new(message, packet.RemoteEndPoint.ToString()))) Interlocked.Increment(ref _dropped);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_cancel.IsCancellationRequested) Error = ex.Message; }
        finally { _messages.Writer.TryComplete(); }
    }
    public void Dispose() { _cancel.Cancel(); _udp.Dispose(); _read.GetAwaiter().GetResult(); _cancel.Dispose(); }
}
