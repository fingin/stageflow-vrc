using System.Net;
using System.Net.Sockets;
using StageFlow.Core.ArtNet;
using StageFlow.Core.Grid;
using StageFlow.Core.Projects;
using StageFlow.Core.Timing;
using StageFlow.Core.Osc;

var tests = new (string Name, Action Body)[]
{
    ("Monitor HLS interface links and private endpoint redaction", MonitorChecks.Links),
    ("Monitor per-route states, failures, codecs and stop transitions", MonitorChecks.States),
    ("RTMP separate keys, raw credentials, legacy URLs and validation", StreamingSettingsChecks.Destinations),
    ("Encoding ranges, GOP timing and backward-compatible persistence", StreamingSettingsChecks.EncodingAndPersistence),
    ("OSC mapping defaults, unique addresses and feedback validation", OscFeedbackChecks.Defaults),
    ("OSC feedback tracks local transport with deduplication and refresh", OscFeedbackChecks.Transport),
    ("OSC feedback values, inverse ranges, targets and wire types", OscFeedbackChecks.Values),
    ("OSC feedback sender delivers UDP and closes cleanly", OscFeedbackChecks.Udp),
    ("OSC feedback project persistence and older v2 defaults", OscFeedbackChecks.Persistence),
    ("OSC wire types, strict padding and malformed packets", () =>
    {
        byte[] packet = OscCodec.Encode("/project/record", 1, 0.5f, true, false, "value", 12L, 0.75d);
        Check(OscCodec.TryDecode(packet, out var decoded)); Equal(1, decoded.Count);
        Equal("/project/record", decoded[0].Address); Equal(1, (int)decoded[0].Arguments[0]!); Equal(0.5f, (float)decoded[0].Arguments[1]!);
        Equal(true, (bool)decoded[0].Arguments[2]!); Equal(false, (bool)decoded[0].Arguments[3]!); Equal(12L, (long)decoded[0].Arguments[5]!);
        for (int length = 0; length < packet.Length; length++) if (length != 16) Check(!OscCodec.TryDecode(packet.AsSpan(0, length), out _)); // Address-only legacy message is valid.
        var random = new Random(741); for (int i = 0; i < 2000; i++) { byte[] noise = new byte[random.Next(0, 512)]; random.NextBytes(noise); OscCodec.TryDecode(noise, out _); }
    }),
    ("OSC nested bundle timestamps and element bounds", () =>
    {
        var message = OscCodec.Encode("/play", 1); byte[] bundle = new byte[20 + message.Length];
        System.Text.Encoding.ASCII.GetBytes("#bundle\0").CopyTo(bundle, 0); bundle[15] = 1;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bundle.AsSpan(16), message.Length); message.CopyTo(bundle, 20);
        Check(OscCodec.TryDecode(bundle, out var decoded)); Equal(1, decoded.Count); Check(decoded[0].Due == null);
        bundle[19] = 255; Check(!OscCodec.TryDecode(bundle, out _));
    }),
    ("OSC mapping supports custom addresses, edges and value ranges", () =>
    {
        var router = new OscRouter(); router.Configure([new() { Address = "/project/record", Action = ControlAction.Play, Trigger = OscTrigger.RisingEdge },
            new() { Address = "/fader", Action = ControlAction.SeekSeconds, Trigger = OscTrigger.Value, Minimum = 10, Maximum = 110 }]);
        Equal(ControlAction.Play, router.Route(new("/project/record", [1]), "pilot").Single().Action);
        Equal(0, router.Route(new("/project/record", [1]), "pilot").Count()); Equal(0, router.Route(new("/project/record", [0]), "pilot").Count());
        Equal(1, router.Route(new("/project/record", [1]), "pilot").Count()); Equal(35d, router.Route(new("/fader", [0.25f]), "pilot").Single().Value);
        Equal(0, router.Route(new("/fader", [float.NaN]), "pilot").Count());
    }),
    ("OSC UDP listener receives and cancels", () =>
    {
        using var reserve = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)); int port = ((IPEndPoint)reserve.Client.LocalEndPoint!).Port; reserve.Close();
        using var listener = new OscListener("127.0.0.1", port); using var sender = new UdpClient();
        var packet = OscCodec.Encode("/play", 1); sender.Send(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, port));
        OscReceived? message = null; Check(SpinWait.SpinUntil(() => listener.TryRead(out message), 3000)); Equal("/play", message!.Message.Address);
    }),
    ("v2 migrates v1 projects and validates protocol codec combinations", () =>
    {
        string path = Path.Combine(Path.GetTempPath(), "stageflow-v1-migration-" + Guid.NewGuid() + ".stageflow");
        File.WriteAllText(path, "{\"schemaVersion\":1,\"name\":\"v1\"}");
        var migrated = ProjectStore.Load(path); Equal(2, migrated.SchemaVersion); Check(File.ReadAllText(path).Contains("\"schemaVersion\":1"));
        var project = new ShowProject { Stream = new() { Encoder = "hevc_nvenc" }, NetworkOutputs = [new() { Enabled = true }] };
        bool rejected = false; try { project.Validate(); } catch (InvalidDataException) { rejected = true; } Check(rejected);
        (project with { Stream = new() { Encoder = "libx264" } }).Validate();
    }),
    ("grid masking hot path does not allocate per channel", () =>
    {
        var layout = new GridLayout { Masks = [new(10, 20)] };
        for (int i = 0; i < 1000; i++) _ = layout.IsMasked(i);
        long before = GC.GetAllocatedBytesForCurrentThread(); int matches = 0;
        for (int update = 0; update < 60; update++)
            for (int channel = 0; channel < 45 * 512; channel++) if (layout.IsMasked(channel)) matches++;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Equal(660, matches); Check(allocated < 4096);
        Console.WriteLine($"  45 universes × 60 updates: {allocated} bytes allocated for masks");
    }),
    ("ArtSync commits universes together and expires safely", () =>
    {
        var store = new DmxUniverseStore(0, 2); byte[] snapshot = new byte[1024];
        store.Apply(new ArtDmx(0, 0, 0, new byte[] { 10, 20 }), 1, 1, 300);
        store.Synchronize(1, 2, 400);
        store.Apply(new ArtDmx(0, 0, 0, new byte[] { 30, 40 }), 1, 3, 300);
        store.Apply(new ArtDmx(1, 0, 0, new byte[] { 50, 60 }), 1, 4, 300);
        store.CopyTo(snapshot, 5); Equal((byte)10, snapshot[0]); Equal((byte)0, snapshot[512]);
        store.Synchronize(2, 6, 400); store.CopyTo(snapshot, 6); Equal((byte)10, snapshot[0]);
        store.Synchronize(1, 7, 400); store.CopyTo(snapshot, 8); Equal((byte)30, snapshot[0]); Equal((byte)50, snapshot[512]);
        store.Apply(new ArtDmx(0, 0, 0, new byte[] { 70, 80 }), 1, 9, 300);
        store.CopyTo(snapshot, 408); Equal((byte)70, snapshot[0]);
    }),
    ("MIDI-DMX banked wire encoding and bounded fair refresh", () =>
    {
        foreach (int channel in Enumerable.Range(0, 16384))
        foreach (byte value in new byte[] { 0, 1, 127, 128, 255 })
        {
            uint message = MidiDmxCodec.Channel(channel, value);
            int decodedChannel = channel / 2048 * 2048 + ((message & 0xf0) == 0x80 ? 1024 : 0) + (int)(message & 15) * 64 + (int)((message >> 9) & 63);
            int decodedValue = (int)((message >> 8) & 1) * 128 + (int)((message >> 16) & 127);
            Equal(channel, decodedChannel); Equal((int)value, decodedValue);
        }
        byte[] desired = new byte[16384]; new Random(437).NextBytes(desired);
        var seen = new bool[16384]; var scheduler = new MidiDmxScheduler(); uint[] messages = new uint[100]; int bank = 0;
        for (int update = 0; update < 400; update++)
        {
            int count = scheduler.Build(desired, messages, 100, 10); Check(count <= 100);
            for (int i = 0; i < count; i++)
            {
                uint m = messages[i];
                if ((m & 255) == 0xbf) { int code = (int)((m >> 16) & 127); if (code < 8) bank = code; continue; }
                int index = bank * 2048 + ((m & 0xf0) == 0x80 ? 1024 : 0) + (int)(m & 15) * 64 + (int)((m >> 9) & 63);
                Equal((int)desired[index], (int)((m >> 8) & 1) * 128 + (int)((m >> 16) & 127)); seen[index] = true;
            }
        }
        Check(seen.All(x => x));
    }),
    ("project rejects invalid styles and nonfinite settings", () =>
    {
        Throws<InvalidDataException>(() => new ShowProject { Layers = [new() { TextColor = "oops" }] }.Validate());
        Throws<InvalidDataException>(() => new ShowProject { Audio = new() { LocalVolume = float.NaN } }.Validate());
        Throws<InvalidDataException>(() => new ShowProject { Stream = new() { SegmentSeconds = double.NaN } }.Validate());
        Throws<InvalidDataException>(() => new ShowProject { Layers = [new() { Kind = LayerKind.Grid }] }.Validate());
    }),
    ("timecode rates and day wrap", () =>
    {
        foreach (var rate in Enum.GetValues<MtcRate>())
        {
            Equal("00:00:00" + (rate == MtcRate.Drop2997 ? ";" : ":") + "00", Timecode.FromFrameNumber(0, rate).ToString());
            Equal(0L, Timecode.FromFrameNumber(rate.FramesPerDay(), rate).ToFrameNumber());
            Equal(rate.FramesPerDay() - 1, Timecode.FromFrameNumber(-1, rate).ToFrameNumber());
            for (long frame = 0; frame < rate.FramesPerDay(); frame += 113)
                Equal(frame, Timecode.FromFrameNumber(frame, rate).ToFrameNumber());
        }
    }),
    ("drop frame minute, ten minute and hour boundaries", () =>
    {
        Equal("00:01:00;02", Timecode.FromFrameNumber(1800, MtcRate.Drop2997).ToString());
        Equal("00:10:00;00", Timecode.FromFrameNumber(17982, MtcRate.Drop2997).ToString());
        Equal("01:00:00;00", Timecode.FromFrameNumber(107892, MtcRate.Drop2997).ToString());
        Check(!Timecode.TryParse("00:01:00;00", MtcRate.Drop2997, out _));
        Check(!Timecode.TryParse("00:01:00;01", MtcRate.Drop2997, out _));
        Check(Timecode.TryParse("00:10:00;00", MtcRate.Drop2997, out _));
    }),
    ("MTC full frame golden wire bytes", () =>
    {
        Span<byte> message = stackalloc byte[10];
        var tc = new Timecode(1, 2, 3, 24, MtcRate.Fps25);
        MtcCodec.WriteFullFrame(tc, message);
        Check(message.SequenceEqual(new byte[] { 0xf0, 0x7f, 0x7f, 1, 1, 0x21, 2, 3, 24, 0xf7 }));
        Check(MtcCodec.TryReadFullFrame(message, out var parsed)); Equal(tc, parsed);
        message[8] = 25; Check(!MtcCodec.TryReadFullFrame(message, out _));
        Check(!MtcCodec.TryReadFullFrame(message[..9], out _));
    }),
    ("MTC quarter-frame ordering and compensation", () =>
    {
        var decoder = new MtcQuarterFrameDecoder();
        var tc = new Timecode(1, 2, 3, 12, MtcRate.Fps30);
        byte[] golden = [0x0c, 0x10, 0x23, 0x30, 0x42, 0x50, 0x61, 0x76];
        for (int i = 0; i < 8; i++) Equal(golden[i], MtcCodec.QuarterFrame(tc, i));
        for (int i = 0; i < 7; i++) Check(!decoder.Push(golden[i], out _));
        Check(decoder.Push(golden[7], out var value)); Equal(tc.AddFrames(2), value);
        for (int i = 7; i > 0; i--) Check(!decoder.Push(golden[i], out _));
        Check(decoder.Push(golden[0], out value)); Equal(tc.AddFrames(-2), value);
        decoder.Reset();
        Check(!decoder.Push(golden[0], out _)); Check(!decoder.Push(golden[2], out _));
        for (int i = 3; i < 8; i++) Check(!decoder.Push(golden[i], out _));
    }),
    ("transport play pause seek loop and end", () =>
    {
        var clock = new ManualClock(); var transport = new Transport(clock);
        transport.Load(TimeSpan.FromSeconds(20)); transport.Play(); clock.Advance(3);
        Equal(TimeSpan.FromSeconds(3), transport.Snapshot().Position);
        transport.Pause(); clock.Advance(10); Equal(TimeSpan.FromSeconds(3), transport.Snapshot().Position);
        transport.SetLoop(true, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
        transport.Play(); clock.Advance(9); Equal(TimeSpan.FromSeconds(3), transport.Snapshot().Position);
        transport.SetLoop(false, TimeSpan.Zero, TimeSpan.Zero); transport.Seek(TimeSpan.FromSeconds(19));
        clock.Advance(4); Equal(TransportState.Paused, transport.Snapshot().State);
        Equal(TimeSpan.FromSeconds(20), transport.Snapshot().Position);
    }),
    ("transport frame stepping does not stick on fractional boundaries", () =>
    {
        foreach (var rate in Enum.GetValues<MtcRate>())
        {
            var t = new Transport(new ManualClock()); t.Load(TimeSpan.FromHours(1)); t.ConfigureClock(ClockMode.Internal, rate, 0);
            for (int i = 1; i <= 500; i++) { t.Step(1); Equal((long)i, t.Snapshot().Timecode.ToFrameNumber()); }
            for (int i = 499; i >= 0; i--) { t.Step(-1); Equal((long)i, t.Snapshot().Timecode.ToFrameNumber()); }
        }
    }),
    ("external MTC locate, follow, local transport exclusion and timeout", () =>
    {
        var clock = new ManualClock(); var t = new Transport(clock); t.Load(TimeSpan.FromMinutes(10));
        t.ConfigureClock(ClockMode.IncomingMtc, MtcRate.Fps30, 30 * 3600);
        t.Play(); Equal(TransportState.WaitingForSync, t.Snapshot().State);
        t.ReceiveMtc(new(1, 0, 10, 0, MtcRate.Fps30), false); Equal(TimeSpan.FromSeconds(10), t.Snapshot().Position);
        t.Seek(TimeSpan.Zero); Equal(TimeSpan.FromSeconds(10), t.Snapshot().Position);
        t.ReceiveMtc(new(1, 0, 10, 0, MtcRate.Fps30), true); clock.Advance(0.2);
        Equal(TimeSpan.FromSeconds(10.2), t.Snapshot().Position);
        clock.Advance(5); Equal(TransportState.SyncLost, t.Snapshot().State);
        Equal(TimeSpan.FromSeconds(10.5), t.Snapshot().Position);
    }),
    ("ArtDmx wire byte order and malformed packets", () =>
    {
        Span<byte> buffer = stackalloc byte[530]; byte[] channels = [1, 128, 254, 255];
        int size = ArtNetPacket.WriteDmx(buffer, 0x1234, 20, channels);
        Equal((byte)0x34, buffer[14]); Equal((byte)0x12, buffer[15]); Equal((byte)0, buffer[16]); Equal((byte)4, buffer[17]);
        Check(ArtNetPacket.TryReadDmx(buffer[..size], out var packet));
        Equal((ushort)0x1234, packet.Universe); Check(packet.Channels.SequenceEqual(channels));
        for (int i = 0; i < size; i++) Check(!ArtNetPacket.TryReadDmx(buffer[..i], out _));
        buffer[17] = 3; Check(!ArtNetPacket.TryReadDmx(buffer[..size], out _));
    }),
    ("Art-Net sequence rollover, zero sequence and short packet", () =>
    {
        var store = new DmxUniverseStore(0, 1); byte[] data = [7, 8];
        Check(store.Apply(new(0, 255, 0, data), 1, 1, 1000));
        Check(store.Apply(new(0, 1, 0, data), 1, 2, 1000));
        Check(!store.Apply(new(0, 255, 0, data), 1, 3, 1000));
        Check(store.Apply(new(0, 0, 0, data), 1, 4, 1000));
        Span<byte> snapshot = stackalloc byte[512]; store.CopyTo(snapshot);
        Equal((byte)7, snapshot[0]); Equal((byte)0, snapshot[511]); Equal(1L, store.OutOfOrder);
    }),
    ("BinaryStageFlight MSB order, CRC and wrap", () =>
    {
        var layout = new GridLayout { Encoding = GridEncoding.BinaryStageFlight, UniverseCount = 1, Width = 16, Height = 8192 };
        var cells = new GridCell[GridEncoder.RequiredCellCapacity(layout)];
        int count = new GridEncoder().Encode(layout, new byte[] { 0x80, 0x01, 0x55, 0xaa, 0xff, 0 }, cells);
        Equal(52, count); Equal((byte)255, cells[0].R); Equal((byte)0, cells[7].R); Equal((byte)255, cells[15].R);
        Equal(192, cells[48].Y); Equal(-1, cells[48].Channel);
        byte[] channels = new byte[25]; channels[24] = 255;
        count = new GridEncoder().Encode(layout, channels, cells);
        Equal(0, cells[24 * 8].X); Equal(208, cells[24 * 8].Y);
        Equal(25 * 8 + 5 * 4, count); // Partial CRC group is safely padded.
    }),
    ("VRSL known positions and RGB packing", () =>
    {
        var layout = new GridLayout { UniverseCount = 9, RgbPacking = true, GammaCorrection = false };
        Equal((0, 0), GridEncoder.VrslPosition(layout, 0)); Equal((624, 64), GridEncoder.VrslPosition(layout, 511));
        Equal((640, 0), GridEncoder.VrslPosition(layout, 512));
        byte[] channels = new byte[9 * 512]; channels[0] = 1; channels[1536] = 2; channels[3072] = 3;
        var cells = new GridCell[GridEncoder.RequiredCellCapacity(layout)];
        Equal(1536, new GridEncoder().Encode(layout, channels, cells));
        Equal((byte)1, cells[0].R); Equal((byte)2, cells[0].G); Equal((byte)3, cells[0].B);
    }),
    ("Hnode import selects serializer, not inactive deserializer", () =>
    {
        const string yaml = "serializer: !BinaryStageFlight {}\ndeserializer: !VRSL {}\ngenerators: []\nexporters:\n- !MIDIDMX\n  midiDevice: loopMIDI Port\n  channelsPerUpdate: 100\n  idleScanChannels: 10\ntranscode: false\nserializeUniverseCount: 2147483647\noutputResolution: 2560x1440\nartNetAddress: 127.0.0.1\ntargetFramerate: 60\n";
        var imported = HnodeImporter.Import(yaml).Project;
        Equal(2560, imported.Width); Equal(1440, imported.Height);
        Equal(GridEncoding.BinaryStageFlight, imported.Layers[1].Grid!.Encoding);
        Equal("loopMIDI Port", imported.MidiDmx.OutputDevice!); Check(imported.ArtNet.AutomaticUniverseCount);
        Equal(45, imported.ArtNet.UniverseCount); Equal(1028, HnodeImporter.ParseChannel("3.5"));
        Throws<InvalidDataException>(() => HnodeImporter.Import(yaml.Replace("transcode: false", "transcode: true")));
        Throws<InvalidDataException>(() => HnodeImporter.Import(yaml.Replace("!BinaryStageFlight", "!Unknown")));
    }),
    ("project atomic save backup and relative media resolution", () =>
    {
        string dir = Path.Combine(Path.GetTempPath(), "StageFlowTests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "show.stageflow"), media = Path.Combine(dir, "movie.mp4");
            var p = new ShowProject { Name = "First", MediaPath = media };
            ProjectStore.Save(p, path); Equal(media, ProjectStore.Load(path).MediaPath!);
            ProjectStore.Save(p with { Name = "Second" }, path);
            Equal("First", ProjectStore.Load(path + ".bak").Name); Equal("Second", ProjectStore.Load(path).Name);
            Check(!File.ReadAllText(path).Contains(dir.Replace("\\", "\\\\")));
        }
        finally { Directory.Delete(dir, true); }
    }),
    ("edit history undo redo and branching", () =>
    {
        var h = new EditHistory<int>(0); h.Apply("one", 1); h.Apply("two", 2);
        Check(h.Undo()); Equal(1, h.Current); Check(h.Redo()); Equal(2, h.Current);
        Check(h.Undo()); h.Apply("three", 3); Check(!h.Redo()); Equal("three", h.UndoName!);
    }),
    ("UDP Art-Net reception and cancellation", () =>
    {
        var store = new DmxUniverseStore(0, 1);
        var receiver = new ArtNetReceiver(store, IPAddress.Loopback, 0); receiver.Start();
        try
        {
            using var udp = new UdpClient(); byte[] packet = new byte[20]; ArtNetPacket.WriteDmx(packet, 0, 1, [10, 20]);
            udp.Send(packet, new IPEndPoint(IPAddress.Loopback, receiver.Port));
            Check(SpinWait.SpinUntil(() => store.Accepted == 1, 2000));
        }
        finally { receiver.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    })
};
int failures = 0;
foreach (var (name, body) in tests)
{
    try { body(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex}"); }
}
if (args.Length == 2 && args[0] == "--hnode")
{
    try { var result = HnodeImporter.Import(File.ReadAllText(args[1])); Console.WriteLine($"PASS actual Hnode import: {result.Project.Width}x{result.Project.Height}, {result.Project.Layers[1].Grid!.Encoding}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine(ex); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} checks passed");
return failures == 0 ? 0 : 1;

static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
static void Equal<T>(T expected, T actual) where T : notnull
{ if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
static void Throws<T>(Action action) where T : Exception
{ try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
sealed class ManualClock : IMonotonicClock
{
    public long Ticks { get; private set; }
    public long Frequency => TimeSpan.TicksPerSecond;
    public void Advance(double seconds) => Ticks += TimeSpan.FromSeconds(seconds).Ticks;
}
