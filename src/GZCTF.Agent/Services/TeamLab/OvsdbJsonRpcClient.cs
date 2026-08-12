using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GZCTF.Agent.Services.TeamLab;

public sealed class OvsdbJsonRpcClient : IDisposable
{
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    static readonly TimeSpan TransactionTimeout = TimeSpan.FromSeconds(15);
    readonly ConcurrentDictionary<string, OvsdbSession> sessions = new(StringComparer.Ordinal);

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

        var key = $"{endpoint}\0{database}";
        var session = sessions.GetOrAdd(key, _ => new OvsdbSession(endpoint, database));
        try
        {
            var result = await session.TransactAsync(operations, cancellationToken);
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
        catch (Exception exception) when (exception is IOException or SocketException or EndOfStreamException or
                                             JsonException or InvalidOperationException or ObjectDisposedException)
        {
            session.Reset();
            throw;
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

    public async Task<IReadOnlyList<JsonArray>> SelectAsync(
        string endpoint,
        string database,
        IReadOnlyList<(string Table, JsonArray Where)> selections,
        CancellationToken cancellationToken)
    {
        if (selections.Count == 0) return [];
        var operations = selections.Select(selection => new JsonObject
        {
            ["op"] = "select",
            ["table"] = selection.Table,
            ["where"] = selection.Where
        }).ToArray();
        var result = await TransactAsync(endpoint, database, operations, cancellationToken);
        if (result is not JsonArray results || results.Count != selections.Count)
            throw new JsonException("OVSDB batch select returned an invalid result count.");
        var rows = new JsonArray[results.Count];
        for (var index = 0; index < results.Count; index++)
            rows[index] = results[index]?["rows"] as JsonArray
                          ?? throw new JsonException($"OVSDB batch select result {index} is invalid.");
        return rows;
    }

    public void Dispose()
    {
        foreach (var session in sessions.Values)
            session.Dispose();
        sessions.Clear();
    }

    sealed class OvsdbSession(string endpoint, string database) : IDisposable
    {
        readonly SemaphoreSlim gate = new(1, 1);
        Socket? socket;
        NetworkStream? stream;
        StreamReader? reader;

        public async Task<JsonNode> TransactAsync(
            IReadOnlyList<JsonObject> operations,
            CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TransactionTimeout);
                var token = deadline.Token;
                await EnsureConnectedAsync(token);

                var requestId = Guid.NewGuid().ToString("N");
                var request = new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["method"] = "transact",
                    ["params"] = new JsonArray { database },
                    ["id"] = requestId
                };
                var parameters = (JsonArray)request["params"]!;
                foreach (var operation in operations)
                    parameters.Add(operation.DeepClone());

                await WriteJsonAsync(request, token);
                var response = await ReadJsonAsync(reader!, token);
                if (!string.Equals(response["id"]?.GetValue<string>(), requestId, StringComparison.Ordinal))
                    throw new JsonException("OVSDB returned a response for a different request.");
                if (response["error"] is not null)
                    throw new InvalidOperationException($"OVSDB transaction failed: {response["error"]}");
                return response["result"]?.DeepClone()
                       ?? throw new InvalidOperationException("OVSDB returned no transaction result.");
            }
            finally
            {
                gate.Release();
            }
        }

        async Task EnsureConnectedAsync(CancellationToken cancellationToken)
        {
            if (socket is not null && stream is not null && reader is not null)
                return;

            var connected = await ConnectAsync(endpoint, cancellationToken);
            var connectedStream = new NetworkStream(connected, ownsSocket: true);
            var connectedReader = new StreamReader(connectedStream, new UTF8Encoding(false), false, 4096,
                leaveOpen: true);
            try
            {
                var greeting = await ReadJsonAsync(connectedReader, cancellationToken);
                if (greeting["method"] is not null && greeting["id"] is { } greetingId)
                {
                    var greetingResponse = new JsonObject
                    {
                        ["jsonrpc"] = "2.0",
                        ["result"] = greeting["params"]?.DeepClone() ?? new JsonArray(),
                        ["id"] = greetingId.DeepClone()
                    };
                    await WriteJsonAsync(connectedStream, greetingResponse, cancellationToken);
                }
                socket = connected;
                stream = connectedStream;
                reader = connectedReader;
            }
            catch
            {
                connectedReader.Dispose();
                connectedStream.Dispose();
                throw;
            }
        }

        async Task WriteJsonAsync(JsonObject value, CancellationToken cancellationToken) =>
            await WriteJsonAsync(stream!, value, cancellationToken);

        static async Task WriteJsonAsync(Stream target, JsonObject value, CancellationToken cancellationToken)
        {
            await target.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions), cancellationToken);
            await target.WriteAsync("\n"u8.ToArray(), cancellationToken);
            await target.FlushAsync(cancellationToken);
        }

        public void Reset()
        {
            reader?.Dispose();
            stream?.Dispose();
            socket?.Dispose();
            reader = null;
            stream = null;
            socket = null;
        }

        public void Dispose()
        {
            Reset();
            gate.Dispose();
        }
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

        if (!TryParseTcpEndpoint(endpoint, out var host, out var port))
            throw new ArgumentException("OVSDB endpoint must use unix:/path or tcp:host:port.", nameof(endpoint));
        var socketFamily = IPAddress.TryParse(host, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6
            ? AddressFamily.InterNetworkV6
            : AddressFamily.InterNetwork;
        var tcp = new Socket(socketFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await tcp.ConnectAsync(host, port, token);
            return tcp;
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    private static bool TryParseTcpEndpoint(string endpoint, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        if (!endpoint.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) return false;
        var value = endpoint[4..];
        if (value.StartsWith("//", StringComparison.Ordinal)) value = value[2..];
        if (value.StartsWith("[", StringComparison.Ordinal))
        {
            var closing = value.IndexOf(']');
            if (closing <= 1 || closing + 2 >= value.Length || value[closing + 1] != ':') return false;
            host = value[1..closing];
            return int.TryParse(value[(closing + 2)..], out port) && port is > 0 and <= 65535;
        }
        var separator = value.LastIndexOf(':');
        if (separator <= 0 || separator == value.Length - 1) return false;
        host = value[..separator];
        return int.TryParse(value[(separator + 1)..], out port) && port is > 0 and <= 65535;
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
