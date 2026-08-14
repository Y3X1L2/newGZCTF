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
    static readonly TimeSpan DefaultTransactionTimeout = TimeSpan.FromSeconds(15);
    static readonly TimeSpan DefaultIdleReconnectThreshold = TimeSpan.FromSeconds(5);
    readonly ConcurrentDictionary<string, OvsdbSession> sessions = new(StringComparer.Ordinal);
    readonly TimeSpan transactionTimeout;
    readonly TimeSpan idleReconnectThreshold;

    public OvsdbJsonRpcClient() : this(DefaultTransactionTimeout, DefaultIdleReconnectThreshold) { }

    internal OvsdbJsonRpcClient(TimeSpan transactionTimeout, TimeSpan idleReconnectThreshold = default)
    {
        if (transactionTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(transactionTimeout));
        this.transactionTimeout = transactionTimeout;
        this.idleReconnectThreshold = idleReconnectThreshold > TimeSpan.Zero
            ? idleReconnectThreshold
            : DefaultIdleReconnectThreshold;
    }

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
        var session = sessions.GetOrAdd(key,
            _ => new OvsdbSession(endpoint, database, transactionTimeout, idleReconnectThreshold));
        var result = await session.TransactAsync(operations, cancellationToken);
        if (result is JsonArray transactionResults)
        {
            for (var index = 0; index < transactionResults.Count; index++)
            {
                if (transactionResults[index] is not JsonObject item || !HasError(item["error"]))
                    continue;
                var table = item["table"]?.GetValue<string>();
                var location = table is null ? $"operation {index + 1}" : $"operation {index + 1} ({table})";
                var error = item["error"]?.GetValue<string>() ?? "operation failed";
                var details = item["details"]?.GetValue<string>();
                throw new InvalidOperationException(
                    $"OVSDB transaction {location} failed: {error}" +
                    (string.IsNullOrEmpty(details) ? string.Empty : $" ({details})"));
            }
        }
        return result;
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

    sealed class OvsdbSession(
        string endpoint,
        string database,
        TimeSpan transactionTimeout,
        TimeSpan idleReconnectThreshold) : IDisposable
    {
        readonly SemaphoreSlim gate = new(1, 1);
        Socket? socket;
        NetworkStream? stream;
        DateTimeOffset lastActivityUtc = DateTimeOffset.UtcNow;

        public async Task<JsonNode> TransactAsync(
            IReadOnlyList<JsonObject> operations,
            CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await TransactCoreAsync(operations, cancellationToken, attempt: 0);
            }
            finally
            {
                gate.Release();
            }
        }

        async Task<JsonNode> TransactCoreAsync(
            IReadOnlyList<JsonObject> operations,
            CancellationToken cancellationToken,
            int attempt)
        {
            try
            {
                if (DateTimeOffset.UtcNow - lastActivityUtc >= idleReconnectThreshold)
                    Reset();
                lastActivityUtc = DateTimeOffset.UtcNow;

                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(transactionTimeout);
                var token = deadline.Token;
                await EnsureConnectedAsync(token);

                var requestId = Guid.NewGuid().ToString("N");
                var request = new JsonObject
                {
                    ["method"] = "transact",
                    ["params"] = new JsonArray { database },
                    ["id"] = requestId
                };
                var parameters = (JsonArray)request["params"]!;
                foreach (var operation in operations)
                    parameters.Add(operation.DeepClone());

                await WriteJsonAsync(request, token);
                while (true)
                {
                    var response = await ReadJsonAsync(stream!, token);
                    if (response["method"] is { } method)
                    {
                        await ReplyToPeerRequestAsync(stream!, method, response["params"], response["id"], token);
                        continue;
                    }
                    if (!string.Equals(response["id"]?.GetValue<string>(), requestId, StringComparison.Ordinal))
                        throw new JsonException("OVSDB returned a response for a different request.");
                    if (HasError(response["error"]))
                        throw new InvalidOperationException("OVSDB transaction failed.");
                    return response["result"]?.DeepClone()
                           ?? throw new InvalidOperationException("OVSDB returned no transaction result.");
                }
            }
            catch (Exception exception) when (attempt == 0 && IsTransportFailure(exception))
            {
                Reset();
                return await TransactCoreAsync(operations, cancellationToken, attempt: 1);
            }
            catch
            {
                Reset();
                throw;
            }
        }

        static bool IsTransportFailure(Exception exception) =>
            exception is IOException or SocketException or EndOfStreamException;

        async Task EnsureConnectedAsync(CancellationToken cancellationToken)
        {
            if (socket is not null && stream is not null && IsSocketConnected(socket))
                return;

            stream?.Dispose();
            socket?.Dispose();
            stream = null;
            socket = null;

            var connected = await ConnectAsync(endpoint, cancellationToken);
            var connectedStream = new NetworkStream(connected, ownsSocket: true);
            socket = connected;
            stream = connectedStream;
        }

        static bool IsSocketConnected(Socket socket)
        {
            try
            {
                // A peer-reset or closed socket reports readable with no pending bytes.
                if (socket.Poll(0, SelectMode.SelectRead))
                    return socket.Available > 0;
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
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

        static Task ReplyToPeerRequestAsync(Stream target, JsonNode method, JsonNode? parameters, JsonNode? id,
            CancellationToken cancellationToken)
        {
            if (id is null)
                return Task.CompletedTask;
            if (!string.Equals(method.GetValue<string>(), "echo", StringComparison.Ordinal))
                throw new JsonException("OVSDB sent an unsupported JSON-RPC request.");
            return WriteJsonAsync(target, new JsonObject
            {
                ["result"] = parameters?.DeepClone() ?? new JsonArray(),
                ["id"] = id.DeepClone()
            }, cancellationToken);
        }

        public void Reset()
        {
            stream?.Dispose();
            socket?.Dispose();
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

    static bool HasError(JsonNode? error) =>
        error is not null && !(error is JsonValue value && value.TryGetValue<object?>(out var raw) && raw is null);

    static async Task<JsonObject> ReadJsonAsync(Stream stream, CancellationToken token)
    {
        // OVSDB is a JSON-RPC byte stream, not a newline-delimited protocol. A server may
        // return a complete JSON object without a trailing newline, so framing follows JSON.
        using var message = new MemoryStream();
        var singleByte = new byte[1];
        var started = false;
        var quoted = false;
        var escaped = false;
        var depth = 0;
        while (true)
        {
            if (await stream.ReadAsync(singleByte, token) == 0)
                throw new EndOfStreamException("OVSDB closed the connection before replying.");

            var value = singleByte[0];
            if (!started)
            {
                if (value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') continue;
                if (value != (byte)'{')
                    throw new JsonException("OVSDB returned a non-object response.");
                started = true;
                depth = 1;
            }
            else if (quoted)
            {
                if (escaped) escaped = false;
                else if (value == (byte)'\\') escaped = true;
                else if (value == (byte)'\"') quoted = false;
            }
            else if (value == (byte)'\"') quoted = true;
            else if (value == (byte)'{') depth++;
            else if (value == (byte)'}') depth--;

            message.WriteByte(value);
            if (started && depth == 0)
                return JsonNode.Parse(message.ToArray()) as JsonObject
                       ?? throw new JsonException("OVSDB returned a non-object response.");
        }
    }
}
