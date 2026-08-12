using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Agent.Services.TeamLab;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class OvsdbJsonRpcClientTests
{
    [Fact]
    public async Task Client_AnswersGreetingAndRejectsMismatchedResponseId()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = ServeOnceAsync(listener, mismatchedResponse: true);
        using var client = new OvsdbJsonRpcClient();

        await Assert.ThrowsAsync<JsonException>(() => client.TransactAsync(
            Endpoint(listener), "Open_vSwitch", [new JsonObject { ["op"] = "select" }],
            CancellationToken.None));
        await server;
    }

    [Fact]
    public async Task Client_ReconnectsAfterThePersistentSessionIsClosed()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = ServeTwiceAsync(listener);
        using var client = new OvsdbJsonRpcClient();
        var operations = new[] { new JsonObject { ["op"] = "select" } };

        Assert.IsType<JsonArray>(await client.TransactAsync(
            Endpoint(listener), "Open_vSwitch", operations, CancellationToken.None));
        await Assert.ThrowsAnyAsync<Exception>(() => client.TransactAsync(
            Endpoint(listener), "Open_vSwitch", operations, CancellationToken.None));
        Assert.IsType<JsonArray>(await client.TransactAsync(
            Endpoint(listener), "Open_vSwitch", operations, CancellationToken.None));
        await server;
    }

    private static string Endpoint(TcpListener listener) =>
        $"tcp:127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";

    private static async Task ServeOnceAsync(TcpListener listener, bool mismatchedResponse)
    {
        using var connection = await listener.AcceptTcpClientAsync();
        await ServeConnectionAsync(connection, mismatchedResponse);
    }

    private static async Task ServeTwiceAsync(TcpListener listener)
    {
        using (var first = await listener.AcceptTcpClientAsync())
            await ServeConnectionAsync(first, mismatchedResponse: false);
        using (var second = await listener.AcceptTcpClientAsync())
            await ServeConnectionAsync(second, mismatchedResponse: false);
    }

    private static async Task ServeConnectionAsync(TcpClient connection, bool mismatchedResponse)
    {
        await using var stream = connection.GetStream();
        using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true)
        {
            AutoFlush = true
        };
        await writer.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"echo\",\"params\":[],\"id\":0}");
        _ = await reader.ReadLineAsync();
        var request = JsonNode.Parse(await reader.ReadLineAsync() ?? throw new EndOfStreamException())!.AsObject();
        var id = mismatchedResponse ? "mismatched" : request["id"]!.GetValue<string>();
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", result = Array.Empty<object>(), id }));
    }
}
