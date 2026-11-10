using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using GZCTF.Modules.TeamLab;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Infrastructure.Netlab;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Runtime;

public sealed class NetlabProxyTests : IAsyncLifetime
{
    private const string Prefix = "/api/teamlab/netlab/game/8";
    private const string Document = "<!doctype html><html><head><base href=\"/\" data-netlab-base/></head><body>环境</body></html>";
    private readonly string _credential = Guid.NewGuid().ToString("N");
    private readonly Channel<ObservedRequest> _requests = Channel.CreateUnbounded<ObservedRequest>();
    private readonly TaskCompletionSource _continueEvents = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _eventsClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _deadline = new(TimeSpan.FromSeconds(20));
    private readonly NetlabWorkspaceContext _workspace = new(Prefix, "practice", "environment-8",
        Guid.NewGuid(), "队伍一", false, []);
    private WebApplication _upstream = null!;
    private WebApplication _gateway = null!;
    private NetlabConnection _connection = null!;
    private HttpClient _browser = null!;

    [Fact]
    public async Task Proxy_AcceptsApiRoutesOnly()
    {
        using var response = await _browser.GetAsync(Prefix + "/environments/environment-8", _deadline.Token);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(_requests.Reader.TryRead(out _));
    }

    [Fact]
    public async Task ApiFiles_PreserveHtmlBytesRangesAndQuery()
    {
        using var full = await _browser.GetAsync(Prefix + "/api/v1/download", _deadline.Token);
        full.EnsureSuccessStatusCode();
        Assert.Equal(Encoding.UTF8.GetBytes(Document), await full.Content.ReadAsByteArrayAsync(_deadline.Token));
        Assert.Equal("attachment", full.Content.Headers.ContentDisposition!.DispositionType);
        await _requests.Reader.ReadAsync(_deadline.Token);
        using var request = new HttpRequestMessage(HttpMethod.Get, Prefix + "/api/v1/download?path=%2Fetc%2Fconfig.html");
        request.Headers.Range = new RangeHeaderValue(4, 35);
        request.Headers.TryAddWithoutValidation("If-Range", "\"file-version\"");
        using var response = await _browser.SendAsync(request, _deadline.Token);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(Encoding.UTF8.GetBytes(Document)[4..36], await response.Content.ReadAsByteArrayAsync(_deadline.Token));
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(32, response.Content.Headers.ContentLength);
        Assert.Equal(4, response.Content.Headers.ContentRange!.From);
        var received = await _requests.Reader.ReadAsync(_deadline.Token);
        Assert.Equal("bytes=4-35", received.Range);
        Assert.Equal("\"file-version\"", received.IfRange);
        Assert.Equal("?path=%2Fetc%2Fconfig.html", received.Query);
    }

    [Theory]
    [InlineData("https://outside.example/api/v1/templates", 400)]
    [InlineData("%2F%2Foutside.example/api/v1/templates", 400)]
    [InlineData("api/v1/%2e%2e%2Fprincipals", 400)]
    [InlineData("api/v1/service-tokens", 403)]
    [InlineData("api%2Fv1%2Fservice-tokens", 403)]
    [InlineData("api%2Fprivate", 404)]
    [InlineData("api/v1/%2573ervice-tokens", 403)]
    public async Task Proxy_RejectsEscapingOrHostManagedPaths(string path, int status)
    {
        using var response = await _browser.GetAsync(Prefix + "/" + path, _deadline.Token);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.False(_requests.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Connection_RejectsTargetsOutsideConfiguredEndpoint()
    {
        foreach (var path in new[] { "https://outside.example/", "//outside.example/", "../api/v1/identity", "/api/v1/identity" })
        {
            var error = Assert.Throws<TeamLabApiContractException>(() => _connection.Request(HttpMethod.Get, path));
            Assert.Equal(400, error.StatusCode);
        }
    }

    [Fact]
    public async Task Proxy_RejectsCrossSiteRequestsBeforeContactingUpstream()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Prefix + "/api/v1/environments");
        request.Headers.Add("Origin", "https://outside.example");
        using var response = await _browser.SendAsync(request, _deadline.Token);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(_requests.Reader.TryRead(out _));
    }

    [Fact]
    public async Task ResourceHeaders_UseWorkspacePrefixBehindUpstreamBasePath()
    {
        using var response = await _browser.GetAsync(Prefix + "/api/v1/created", _deadline.Token);
        Assert.Equal(Prefix + "/api/v1/environments/new", response.Headers.Location!.ToString());
        Assert.Equal(Prefix + "/api/v1/operations/start", response.Headers.GetValues("Operation-Location").Single());
    }

    [Theory]
    [InlineData("events", true)]
    [InlineData("assets/container/logs", false)]
    public async Task Events_ArriveBeforeStreamCompletesAndDisconnectClosesUpstream(string suffix, bool redact)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Prefix + "/api/v1/environments/environment-8/" + suffix);
        request.Headers.Add("Last-Event-ID", "previous-event");
        using var response = await _browser.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _deadline.Token);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(_deadline.Token);
        using var reader = new StreamReader(stream);
        Assert.Equal("event: runtime.changed", await reader.ReadLineAsync(_deadline.Token));
        Assert.Equal(redact ? "data: {}" : "data: first", await reader.ReadLineAsync(_deadline.Token));
        Assert.Equal("", await reader.ReadLineAsync(_deadline.Token));
        _continueEvents.SetResult();
        Assert.Equal("event: runtime.changed", await reader.ReadLineAsync(_deadline.Token));
        Assert.Equal(redact ? "data: {}" : "data: second", await reader.ReadLineAsync(_deadline.Token));
        var received = await _requests.Reader.ReadAsync(_deadline.Token);
        Assert.Equal("previous-event", received.LastEventId);
        AssertServerIdentity(received);
        stream.Dispose();
        _browser.Dispose();
        await _eventsClosed.Task.WaitAsync(_deadline.Token);
    }

    [Fact]
    public async Task WebSocket_RelaysFragmentedBinaryAndCloseWithServerIdentity()
    {
        using var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol("binary");
        socket.Options.SetRequestHeader("Authorization", "Bearer browser-identity");
        socket.Options.SetRequestHeader("X-Netlab-Context", "browser-context");
        socket.Options.SetRequestHeader("Cookie", "session=browser-session");
        await socket.ConnectAsync(new UriBuilder(_browser.BaseAddress!) { Scheme = "ws", Path = Prefix + "/api/v1/socket" }.Uri, _deadline.Token);
        Assert.Equal("binary", socket.SubProtocol);
        var payload = new byte[100_000];
        Random.Shared.NextBytes(payload);
        await socket.SendAsync(payload.AsMemory(0, 40_000), WebSocketMessageType.Binary, false, _deadline.Token);
        await socket.SendAsync(payload.AsMemory(40_000), WebSocketMessageType.Binary, true, _deadline.Token);
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        ValueWebSocketReceiveResult frame;
        do
        {
            frame = await socket.ReceiveAsync(buffer.AsMemory(), _deadline.Token);
            Assert.Equal(WebSocketMessageType.Binary, frame.MessageType);
            result.Write(buffer, 0, frame.Count);
        } while (!frame.EndOfMessage);
        Assert.Equal(payload, result.ToArray());
        AssertServerIdentity(await _requests.Reader.ReadAsync(_deadline.Token));
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", _deadline.Token);
        Assert.Equal(WebSocketState.Closed, socket.State);
    }

    public async Task InitializeAsync()
    {
        var upstream = Builder();
        _upstream = upstream.Build();
        _upstream.UseWebSockets();
        _upstream.Run(ServeUpstreamAsync);
        await _upstream.StartAsync(_deadline.Token);

        var gateway = Builder();
        gateway.Services.AddTeamLabModule();
        gateway.Services.RemoveAll<IHostedService>();
        _gateway = gateway.Build();
        _gateway.UseWebSockets();
        var protection = new EphemeralDataProtectionProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Netlab:Endpoint"] = _upstream.Urls.Single() + "/base/",
            ["Netlab:ProjectId"] = "practice",
            ["Netlab:Credential"] = protection.CreateProtector("Netlab.ServiceCredential").Protect(_credential)
        }).Build();
        var clients = _gateway.Services.GetRequiredService<IHttpClientFactory>();
        _connection = new NetlabConnection(configuration, null!, protection, clients);
        var proxy = new NetlabProxy(_connection, clients);
        _gateway.Map(Prefix + "/{**path}", async context =>
        {
            try { await proxy.ForwardAsync(context, _workspace, NetlabProxy.ValidateRequest(context.Request, context.Request.RouteValues["path"]?.ToString() ?? "")); }
            catch (TeamLabApiContractException error)
            {
                context.Response.StatusCode = error.StatusCode;
                await context.Response.WriteAsync(error.Code, context.RequestAborted);
            }
        });
        await _gateway.StartAsync(_deadline.Token);
        _browser = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
        { BaseAddress = new Uri(_gateway.Urls.Single()), Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task DisposeAsync()
    {
        _browser?.Dispose();
        _continueEvents.TrySetResult();
        if (_gateway is not null) await _gateway.DisposeAsync();
        if (_upstream is not null) await _upstream.DisposeAsync();
        _deadline.Dispose();
    }

    private static WebApplicationBuilder Builder()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        return builder;
    }

    private void AssertServerIdentity(ObservedRequest received)
    {
        Assert.Equal("Bearer " + _credential, received.Authorization);
        Assert.Empty(received.Cookie);
        Assert.Empty(received.Scope);
    }

    private async Task ServeUpstreamAsync(HttpContext context)
    {
        var request = context.Request;
        await _requests.Writer.WriteAsync(new(request.Path, request.QueryString.ToString(), request.Headers.Authorization.ToString(),
            request.Headers.Cookie.ToString(), request.Headers["X-Netlab-Context"].ToString(), request.Headers.Range.ToString(),
            request.Headers.IfRange.ToString(), request.Headers["Last-Event-ID"].ToString()), context.RequestAborted);
        if (context.WebSockets.IsWebSocketRequest)
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync("binary");
            var buffer = new byte[16_384];
            while (socket.State == WebSocketState.Open)
            {
                var frame = await socket.ReceiveAsync(buffer.AsMemory(), context.RequestAborted);
                if (frame.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseOutputAsync(socket.CloseStatus!.Value, socket.CloseStatusDescription, context.RequestAborted);
                    return;
                }
                await socket.SendAsync(buffer.AsMemory(0, frame.Count), frame.MessageType, frame.EndOfMessage, context.RequestAborted);
            }
            return;
        }
        if (request.Path is { Value: "/base/api/v1/environments/environment-8/events" or
            "/base/api/v1/environments/environment-8/assets/container/logs" })
        {
            using var disconnected = context.RequestAborted.Register(() => _eventsClosed.TrySetResult());
            context.Response.ContentType = "text/event-stream";
            await context.Response.WriteAsync("event: runtime.changed\ndata: first\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            await _continueEvents.Task.WaitAsync(context.RequestAborted);
            await context.Response.WriteAsync("event: runtime.changed\ndata: second\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            await _eventsClosed.Task.WaitAsync(_deadline.Token);
            return;
        }
        if (request.Path == "/base/api/v1/environments")
        {
            context.Response.ContentType = request.ContentType;
            await request.Body.CopyToAsync(context.Response.Body, context.RequestAborted);
            return;
        }
        if (request.Path == "/base/api/v1/created")
        {
            context.Response.StatusCode = StatusCodes.Status201Created;
            context.Response.Headers.Location = "/api/v1/environments/new";
            context.Response.Headers["Operation-Location"] = "/base/api/v1/operations/start";
            return;
        }
        context.Response.ContentType = "text/html; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(Document);
        if (request.Path == "/base/api/v1/download")
        {
            context.Response.Headers.ContentDisposition = "attachment; filename=config.html";
            if (request.Headers.Range.Count > 0)
            {
                context.Response.StatusCode = StatusCodes.Status206PartialContent;
                context.Response.Headers.ContentRange = $"bytes 4-35/{bytes.Length}";
                bytes = bytes[4..36];
            }
        }
        context.Response.Headers.SetCookie = "upstream-session=value";
        context.Response.ContentLength = bytes.Length;
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
    }

    private sealed record ObservedRequest(string Path, string Query, string Authorization, string Cookie, string Scope,
        string Range, string IfRange, string LastEventId);
}
