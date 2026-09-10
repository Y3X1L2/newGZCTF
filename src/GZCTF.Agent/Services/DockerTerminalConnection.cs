using System.IO.Pipes;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;

namespace GZCTF.Agent.Services;

internal sealed class DockerTerminalConnection(HttpClient client, HttpResponseMessage response, Stream stream) : IDisposable
{
    public Stream Stream { get; } = stream;

    public static async Task<DockerTerminalConnection> OpenAsync(Uri endpoint, string execId, CancellationToken token)
    {
        var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(10) };
        var baseUri = endpoint;
        if (endpoint.Scheme == "unix")
        {
            baseUri = new Uri("http://docker/");
            handler.ConnectCallback = async (_, ct) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint.AbsolutePath), ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            };
        }
        else if (endpoint.Scheme == "npipe")
        {
            baseUri = new Uri("http://docker/");
            var pipeName = endpoint.AbsolutePath;
            if (!pipeName.StartsWith("/pipe/", StringComparison.Ordinal))
                throw new NotSupportedException("Invalid Docker named pipe endpoint.");
            handler.ConnectCallback = async (_, ct) =>
            {
                var pipe = new NamedPipeClientStream(endpoint.Host, pipeName[6..], PipeDirection.InOut, PipeOptions.Asynchronous);
                try { await pipe.ConnectAsync(ct); return pipe; }
                catch { pipe.Dispose(); throw; }
            };
        }
        else if (endpoint.Scheme == "tcp") baseUri = new UriBuilder(endpoint) { Scheme = "http" }.Uri;
        else if (endpoint.Scheme is not ("http" or "https"))
            throw new NotSupportedException("Unsupported Docker terminal endpoint.");

        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        HttpResponseMessage? response = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, $"/exec/{Uri.EscapeDataString(execId)}/start"))
            {
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact,
                Content = JsonContent.Create(new { Detach = false, Tty = true })
            };
            request.Headers.Connection.Add("Upgrade");
            request.Headers.Upgrade.ParseAdd("tcp");
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode != HttpStatusCode.SwitchingProtocols)
                throw new IOException($"Docker terminal upgrade was rejected with HTTP {(int)response.StatusCode}.");
            var stream = await response.Content.ReadAsStreamAsync(token);
            if (!stream.CanWrite) throw new IOException("Docker terminal transport is not duplex.");
            return new(client, response, stream);
        }
        catch { response?.Dispose(); client.Dispose(); throw; }
    }

    public void Dispose()
    {
        Stream.Dispose();
        response.Dispose();
        client.Dispose();
    }
}
