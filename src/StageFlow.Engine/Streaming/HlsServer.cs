using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;

namespace StageFlow.Engine.Streaming;

/// <summary>In-process HTTP/HLS endpoint; no separate service or process is launched.</summary>
public sealed class HlsServer : IAsyncDisposable
{
    private WebApplication? _application;
    private long _requests, _bytesServed;
    public long Requests => Interlocked.Read(ref _requests);
    public long BytesServed => Interlocked.Read(ref _bytesServed);
    public IReadOnlyCollection<string> Addresses => _application?.Urls.ToArray() ?? [];
    public async Task StartAsync(HlsSegmentStore store, IPAddress address, int port, string webAssetsDirectory,
        CancellationToken cancellationToken = default)
    {
        if (_application != null) throw new InvalidOperationException("HLS server already running.");
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ApplicationName = typeof(HlsServer).Assembly.FullName });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(address, port, endpoint => endpoint.Protocols = HttpProtocols.Http1AndHttp2);
            options.Limits.MaxRequestBodySize = 0;
            options.Limits.MaxConcurrentConnections = 128;
            options.AddServerHeader = false;
        });
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            Interlocked.Increment(ref _requests);
            context.Response.Headers.AccessControlAllowOrigin = "*";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            await next(context).ConfigureAwait(false);
        });
        app.MapMethods("/live/index.m3u8", ["GET", "HEAD"], () =>
        {
            string? playlist = store.Playlist();
            return playlist == null ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable) :
                Results.Text(playlist, "application/vnd.apple.mpegurl");
        });
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.Value?.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) == true)
                context.Response.Headers.CacheControl = "no-store";
            await next(context).ConfigureAwait(false);
        });
        app.MapMethods("/live/segment-{sequence:long}.ts", ["GET", "HEAD"], (long sequence) =>
        {
            HlsSegment? segment = store.Get(sequence);
            if (segment == null) return Results.NotFound();
            Interlocked.Add(ref _bytesServed, segment.Data.Length);
            return Results.File(segment.Data, "video/mp2t", enableRangeProcessing: true);
        });
        app.MapGet("/", () => Results.File(Path.Combine(webAssetsDirectory, "index.html"), "text/html"));
        app.MapGet("/hls.min.js", () => Results.File(Path.Combine(webAssetsDirectory, "hls.min.js"), "application/javascript"));
        app.MapGet("/health", () => Results.Json(new { ready = store.SegmentCount > 0, segments = store.SegmentCount }));
        try { await app.StartAsync(cancellationToken).ConfigureAwait(false); _application = app; }
        catch { await app.DisposeAsync().ConfigureAwait(false); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        if (_application == null) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await _application.StopAsync(timeout.Token).ConfigureAwait(false); }
        finally { await _application.DisposeAsync().ConfigureAwait(false); _application = null; }
    }
}
