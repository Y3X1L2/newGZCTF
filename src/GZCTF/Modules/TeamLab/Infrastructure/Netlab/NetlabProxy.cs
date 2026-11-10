using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using GZCTF.Modules.TeamLab.Application;
using Microsoft.AspNetCore.Http.Features;

namespace GZCTF.Modules.TeamLab.Infrastructure.Netlab;

public sealed class NetlabProxy(NetlabConnection connection, IHttpClientFactory clients)
{
    private static readonly HashSet<string> RequestHeaders = new(StringComparer.OrdinalIgnoreCase)
        { "Accept", "Accept-Language", "Range", "If-Range", "If-None-Match", "If-Modified-Since", "Last-Event-ID" };
    private static readonly HashSet<string> ExcludedResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
        { "Connection", "Transfer-Encoding", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization", "Trailer", "Upgrade", "Set-Cookie" };

    public async Task ForwardAsync(HttpContext context, NetlabWorkspaceContext workspace, string path)
    {
        ValidateRequest(context.Request, path);
        var token = context.RequestAborted;
        using var headersDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var request = connection.Request(new HttpMethod(context.Request.Method), path + context.Request.QueryString, workspace.Scope);
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
            if (path == "api/v1/environments" && HttpMethods.IsPost(context.Request.Method))
            {
                var input = await JsonNode.ParseAsync(context.Request.Body, cancellationToken: token)
                    ?? throw new TeamLabApiContractException("invalid_environment", "环境配置不能为空。", 400);
                input["projectId"] = workspace.ProjectId;
                request.Content = JsonContent.Create(input, options: NetlabClient.Json);
            }
        }
        if (request.Content is not UploadContent) headersDeadline.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await clients.CreateClient("NetlabStream").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersDeadline.Token);
        headersDeadline.CancelAfter(Timeout.InfiniteTimeSpan);
        var html = !path.StartsWith("api/", StringComparison.Ordinal) && response.Content.Headers.ContentType?.MediaType == "text/html";
        context.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers.Concat(response.Content.Headers))
            if (!ExcludedResponseHeaders.Contains(header.Key) && !(html && header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)))
                context.Response.Headers[header.Key] = header.Value.ToArray();
        if (response.Headers.Location is { } location)
        {
            if (location.IsAbsoluteUri && location.GetLeftPart(UriPartial.Authority) != request.RequestUri!.GetLeftPart(UriPartial.Authority))
                throw new TeamLabApiContractException("unexpected_redirect", "底座返回了其他站点的跳转。", 502);
            context.Response.Headers.Location = workspace.BasePath + "/" + (location.IsAbsoluteUri ? location.PathAndQuery : location.ToString()).TrimStart('/');
        }
        context.Response.Headers.CacheControl = "private, no-store";
        if (html)
        {
            var document = await response.Content.ReadAsStringAsync(token);
            var host = JsonSerializer.Serialize(new { basePath = workspace.BasePath, theme = "light", environmentOnly = workspace.EnvironmentId is not null }, NetlabClient.Json);
            document = document.Replace("<base href=\"/\" data-netlab-base/>", $"<base href=\"{workspace.BasePath}/\" data-netlab-base/>", StringComparison.Ordinal)
                .Replace("</head>", $"<script type=\"application/json\" id=\"netlab-host\">{host}</script></head>", StringComparison.Ordinal);
            context.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'self'";
            await context.Response.WriteAsync(document, token);
            return;
        }
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        await response.Content.CopyToAsync(context.Response.Body, token);
    }

    private static void ValidateRequest(HttpRequest request, string path)
    {
        var decoded = Uri.UnescapeDataString(path);
        if (decoded.IndexOfAny(['\\', ':', '?', '#']) >= 0 || decoded.Split('/').Any(segment => segment is "." or "..") || decoded.StartsWith('/'))
            throw new TeamLabApiContractException("invalid_path", "请求路径无效。", 400);
        path = decoded;
        if (path.StartsWith("api/") && !path.StartsWith("api/v1/"))
            throw new TeamLabApiContractException("invalid_api", "请使用底座公共接口。", 404);
        if (path.StartsWith("api/v1/principals") || path.StartsWith("api/v1/service-tokens") ||
            path.StartsWith("api/v1/sessions") || path == "api/v1/identity/password" || path.EndsWith("/grants"))
            throw new TeamLabApiContractException("host_managed_identity", "账号与授权由平台管理。", 403);
        if (request.Headers["Sec-Fetch-Site"] == "cross-site" ||
            request.Headers.Origin is { Count: > 0 } origin &&
            (!Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var uri) ||
             !string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase)))
            throw new TeamLabApiContractException("cross_origin_denied", "跨站请求被拒绝。", 403);
    }

    private static async Task SocketAsync(HttpContext context, HttpRequestMessage request, CancellationToken token)
    {
        using var remote = new ClientWebSocket();
        foreach (var header in request.Headers) remote.Options.SetRequestHeader(header.Key, string.Join(",", header.Value));
        foreach (var protocol in context.WebSockets.WebSocketRequestedProtocols) remote.Options.AddSubProtocol(protocol);
        var address = new UriBuilder(request.RequestUri!) { Scheme = request.RequestUri!.Scheme == "https" ? "wss" : "ws" };
        await remote.ConnectAsync(address.Uri, token);
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

    private sealed class UploadContent(Stream source, Action sent) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            await SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context, CancellationToken token)
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

