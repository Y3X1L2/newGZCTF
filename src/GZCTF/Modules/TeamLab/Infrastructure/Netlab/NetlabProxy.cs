using System.Net;
using System.Net.WebSockets;
using GZCTF.Modules.TeamLab.Application;
using Microsoft.AspNetCore.Http.Features;

namespace GZCTF.Modules.TeamLab.Infrastructure.Netlab;

public sealed class NetlabProxy(NetlabConnection connection, IHttpClientFactory clients)
{
    private static readonly HashSet<string> RequestHeaders = new(StringComparer.OrdinalIgnoreCase)
        { "Accept", "Accept-Language", "Range", "If-Range", "If-None-Match", "If-Modified-Since", "Last-Event-ID" };
    private static readonly HashSet<string> ExcludedResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
        { "Connection", "Transfer-Encoding", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization", "Trailer", "Upgrade", "Set-Cookie" };

    public async Task ForwardAsync(HttpContext context, NetlabWorkspaceContext workspace, string path, CancellationToken session = default)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, session);
        var token = lifetime.Token;
        using var headersDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var request = connection.Request(new HttpMethod(context.Request.Method), path + context.Request.QueryString);
        if (context.WebSockets.IsWebSocketRequest)
        {
            await SocketAsync(context, request, token);
            return;
        }
        foreach (var header in context.Request.Headers.Where(header => RequestHeaders.Contains(header.Key)))
            request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            request.Content = new UploadContent(context.Request.Body, () => headersDeadline.CancelAfter(TimeSpan.FromSeconds(30)));
            if (context.Request.ContentType is { } contentType)
                request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            if (context.Request.ContentLength is { } length) request.Content.Headers.ContentLength = length;
        }
        if (request.Content is not UploadContent) headersDeadline.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await clients.CreateClient("NetlabStream").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersDeadline.Token);
        headersDeadline.CancelAfter(Timeout.InfiniteTimeSpan);
        context.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers.Concat(response.Content.Headers))
            if (!ExcludedResponseHeaders.Contains(header.Key))
                context.Response.Headers[header.Key] = header.Value.ToArray();
        foreach (var header in new[] { "Location", "Operation-Location" })
        {
            if (!response.Headers.TryGetValues(header, out var values)) continue;
            var target = new Uri(request.RequestUri!, values.Single());
            var root = connection.Endpoint;
            if (root.GetLeftPart(UriPartial.Authority) != target.GetLeftPart(UriPartial.Authority))
                throw new TeamLabApiContractException("unexpected_redirect", "底座返回了其他站点的跳转。", 502);
            context.Response.Headers[header] = workspace.BasePath + "/" +
                (root.IsBaseOf(target) ? root.MakeRelativeUri(target).ToString() : target.PathAndQuery.TrimStart('/'));
        }
        context.Response.Headers.CacheControl = "private, no-store";
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        if (workspace.EnvironmentId is not null && response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(token));
            while (await reader.ReadLineAsync(token) is { } line)
            {
                await context.Response.WriteAsync((line.StartsWith("data:", StringComparison.Ordinal) ? "data: {}" : line) + "\n", token);
                if (line.Length == 0) await context.Response.Body.FlushAsync(token);
            }
        }
        else await response.Content.CopyToAsync(context.Response.Body, token);
    }

    public static string ValidateRequest(HttpRequest request, string path)
    {
        var decoded = Uri.UnescapeDataString(path);
        if (decoded.IndexOfAny(['\\', ':', '?', '#', '%']) >= 0 || decoded.Split('/').Any(segment => segment is "." or "..") || decoded.StartsWith('/'))
            throw new TeamLabApiContractException("invalid_path", "请求路径无效。", 400);
        path = decoded;
        if (!path.StartsWith("api/v1/", StringComparison.Ordinal))
            throw new TeamLabApiContractException("invalid_api", "请使用底座公共接口。", 404);
        if (path.StartsWith("api/v1/principals") || path.StartsWith("api/v1/service-tokens") ||
            path.StartsWith("api/v1/sessions") || path == "api/v1/identity/password" || path.EndsWith("/grants"))
            throw new TeamLabApiContractException("host_managed_identity", "账号与授权由平台管理。", 403);
        if (request.Headers["Sec-Fetch-Site"] == "cross-site" ||
            request.Headers.Origin is { Count: > 0 } origin &&
            (!Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var uri) ||
             !string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase)))
            throw new TeamLabApiContractException("cross_origin_denied", "跨站请求被拒绝。", 403);
        return path;
    }

    private static async Task SocketAsync(HttpContext context, HttpRequestMessage request, CancellationToken token)
    {
        using var remote = new ClientWebSocket();
        foreach (var header in request.Headers) remote.Options.SetRequestHeader(header.Key, string.Join(",", header.Value));
        foreach (var protocol in context.WebSockets.WebSocketRequestedProtocols) remote.Options.AddSubProtocol(protocol);
        var address = new UriBuilder(request.RequestUri!) { Scheme = request.RequestUri!.Scheme == "https" ? "wss" : "ws" };
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            await remote.ConnectAsync(address.Uri, deadline.Token);
        }
        using var local = await context.WebSockets.AcceptWebSocketAsync(remote.SubProtocol);
        using var closed = CancellationTokenSource.CreateLinkedTokenSource(token);
        var upstream = RelayAsync(local, remote, closed.Token);
        var downstream = RelayAsync(remote, local, closed.Token);
        var finished = await Task.WhenAny(upstream, downstream);
        if (finished.IsFaulted || finished.IsCanceled) await closed.CancelAsync();
        else closed.CancelAfter(TimeSpan.FromSeconds(5));
        try { await Task.WhenAll(upstream, downstream); }
        catch (OperationCanceledException) when (closed.IsCancellationRequested) { }
    }

    private static async Task RelayAsync(WebSocket source, WebSocket target, CancellationToken token)
    {
        var buffer = new byte[32 * 1024];
        while (source.State == WebSocketState.Open)
        {
            var frame = await source.ReceiveAsync(buffer.AsMemory(), token);
            if (frame.MessageType == WebSocketMessageType.Close)
            {
                if (target.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    await target.CloseOutputAsync(source.CloseStatus ?? WebSocketCloseStatus.NormalClosure, source.CloseStatusDescription, token);
                return;
            }
            await target.SendAsync(buffer.AsMemory(0, frame.Count), frame.MessageType, frame.EndOfMessage, token);
        }
    }

    private sealed class UploadContent(System.IO.Stream source, Action sent) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(System.IO.Stream stream, System.Net.TransportContext? context) =>
            await SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(System.IO.Stream stream, System.Net.TransportContext? context, CancellationToken token)
        {
            await source.CopyToAsync(stream, token);
            sent();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
