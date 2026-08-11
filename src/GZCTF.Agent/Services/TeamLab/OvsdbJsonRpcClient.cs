using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GZCTF.Agent.Services.TeamLab;

public sealed class OvsdbJsonRpcClient(ILogger<OvsdbJsonRpcClient> logger)
{
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    readonly SemaphoreSlim transactionLock = new(1, 1);

    public async Task<JsonNode> TransactAsync(
        string endpoint,
        string database,
        IReadOnlyList<JsonObject> operations,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(database))
            throw new ArgumentException("OVSDB endpoint and database are required.");
        if (operations.Count == 0)
            throw new ArgumentException("An OVSDB transaction must contain an operation.");

        await transactionLock.WaitAsync(cancellationToken);
        try
        {
            using var socket = await ConnectAsync(endpoint, cancellationToken);
            await using var stream = new NetworkStream(socket, ownsSocket: false);
            using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, leaveOpen: true);

            // OVSDB sends a JSON-RPC greeting as soon as the connection opens. It
            // must be consumed before the transaction request, otherwise the first
            // response read is mistaken for the greeting and the transaction result
            // is lost. Echo-style greetings also require a JSON-RPC response.
            var greeting = await ReadJsonAsync(reader, cancellationToken);
            if (greeting["method"]?.GetValue<string>() is { } greetingMethod &&
                greeting["id"] is { } greetingId)
            {
                var greetingResponse = new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["result"] = greeting["params"]?.DeepClone() ?? new JsonArray(),
                    ["id"] = greetingId.DeepClone()
                };
                await stream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(greetingResponse, JsonOptions), cancellationToken);
                await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
                await stream.FlushAsync(cancellationToken);
                logger.LogTrace("Answered OVSDB greeting method {Method}.", greetingMethod);
            }
            var request = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "transact",
                ["params"] = new JsonArray { database }
            };
            var parameters = (JsonArray)request["params"]!;
            foreach (var operation in operations)
                parameters.Add(operation);
            request["id"] = Guid.NewGuid().ToString("N");

            var payload = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
            await stream.WriteAsync(payload, cancellationToken);
            await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
            await stream.FlushAsync(cancellationToken);

            var response = await ReadJsonAsync(reader, cancellationToken);
            if (response["error"] is not null)
            {
                logger.LogDebug("OVSDB transaction returned an error: {Error}", response["error"]);
                throw new InvalidOperationException($"OVSDB transaction failed: {response["error"]}");
            }
            var result = response["result"]?.DeepClone()
                         ?? throw new InvalidOperationException("OVSDB returned no transaction result.");
            if (result is JsonArray transactionResults)
            {
                var failed = transactionResults
                    .OfType<JsonObject>()
                    .FirstOrDefault(item => item["error"] is not null);
                if (failed is not null)
                    throw new InvalidOperationException($"OVSDB transaction operation failed: {failed}");
            }
            return result;
        }
        finally
        {
            transactionLock.Release();
        }
    }

    public async Task<JsonArray> SelectAsync(
        string endpoint,
        string database,
        string table,
        JsonArray where,
        CancellationToken cancellationToken)
    {
        var result = await TransactAsync(endpoint, database,
            [new JsonObject
            {
                ["op"] = "select",
                ["table"] = table,
                ["where"] = where
            }], cancellationToken);
        if (result is not JsonArray results || results.Count != 1 ||
            results[0]?["rows"] is not JsonArray rows)
            throw new JsonException($"OVSDB select on {table} returned an invalid result.");
        return rows;
    }

    static async Task<Socket> ConnectAsync(string endpoint, CancellationToken token)
    {
        if (endpoint.StartsWith("unix:", StringComparison.OrdinalIgnoreCase))
        {
            var path = endpoint[5..];
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), token);
                return socket;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        var uri = new Uri(endpoint);
        var tcp = new Socket(uri.HostNameType == UriHostNameType.IPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork,
            SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await tcp.ConnectAsync(uri.Host, uri.Port, token);
            return tcp;
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    static async Task<JsonObject> ReadJsonAsync(StreamReader reader, CancellationToken token)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(token);
            if (line is null)
                throw new EndOfStreamException("OVSDB closed the connection before replying.");
            if (string.IsNullOrWhiteSpace(line)) continue;
            return JsonNode.Parse(line) as JsonObject
                   ?? throw new JsonException("OVSDB returned a non-object response.");
        }
    }
}
