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
    public async Task Client_SendsRequestWithoutWaitingForServerGreeting()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = ServeOnceAsync(listener, mismatchedResponse: false);
        using var client = new OvsdbJsonRpcClient();

        Assert.IsType<JsonArray>(await client.TransactAsync(
            Endpoint(listener), "Open_vSwitch", [new JsonObject { ["op"] = "select" }],
            CancellationToken.None));
        await server;
    }

    [Fact]
    public async Task Client_AnswersEchoRequestBeforeReadingTransactionResponse()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = ServeEchoThenResponseAsync(listener);
        using var client = new OvsdbJsonRpcClient();

        Assert.IsType<JsonArray>(await client.TransactAsync(
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
        Assert.IsType<JsonArray>(await client.TransactAsync(
            Endpoint(listener), "Open_vSwitch", operations, CancellationToken.None));
        await server;
    }

    [Fact]
    public async Task Client_ReconnectsBeforeReusingAnIdleSession()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = ServeTwiceAsync(listener);
        using var client = new OvsdbJsonRpcClient(TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(50));
        var operations = new[] { new JsonObject { ["op"] = "select" } };

        Assert.IsType<JsonArray>(await client.TransactAsync(
            Endpoint(listener), "Open_vSwitch", operations, CancellationToken.None));
        await Task.Delay(100);
        Assert.IsType<JsonArray>(await client.TransactAsync(
            Endpoint(listener), "Open_vSwitch", operations, CancellationToken.None));
        await server;
    }
    [Fact]
    public async Task Client_ResetsTimedOutSessionBeforeNextTransactionUsesIt()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var firstRequestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = ServeTimedOutThenSuccessfulConnectionAsync(listener, firstRequestReceived, releaseFirst);
        using var client = new OvsdbJsonRpcClient();
        using var timeout = new CancellationTokenSource();
        var operations = new[] { new JsonObject { ["op"] = "select" } };

        var first = client.TransactAsync(Endpoint(listener), "Open_vSwitch", operations, timeout.Token);
        await firstRequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        timeout.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        releaseFirst.SetResult();
        Assert.IsType<JsonArray>(await client.TransactAsync(
            Endpoint(listener), "Open_vSwitch", operations, CancellationToken.None));
        await server.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Client_ReportsOperationErrorWithNonStringDetails()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = ServeOperationErrorAsync(listener);
        using var client = new OvsdbJsonRpcClient();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.TransactAsync(
            Endpoint(listener), "Open_vSwitch", [new JsonObject { ["op"] = "select" }],
            CancellationToken.None));

        Assert.Equal("OVSDB transaction operation 1 failed: constraint violation", exception.Message);
        await server;
    }

    [Fact]
    public async Task Client_DoesNotExposeOvsdbErrorPayload()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = ServeErrorAsync(listener);
        using var client = new OvsdbJsonRpcClient();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.TransactAsync(
            Endpoint(listener), "Open_vSwitch", [new JsonObject { ["op"] = "select" }],
            CancellationToken.None));

        Assert.Equal("OVSDB transaction failed.", exception.Message);
        Assert.DoesNotContain("secret-table-detail", exception.Message, StringComparison.Ordinal);
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

    private static async Task ServeTimedOutThenSuccessfulConnectionAsync(
        TcpListener listener,
        TaskCompletionSource firstRequestReceived,
        TaskCompletionSource releaseFirst)
    {
        using (var first = await listener.AcceptTcpClientAsync())
        {
            using var reader = new StreamReader(first.GetStream(), new UTF8Encoding(false), false, 4096,
                leaveOpen: true);
            _ = await reader.ReadLineAsync();
            firstRequestReceived.SetResult();
            await releaseFirst.Task;
        }
        using (var second = await listener.AcceptTcpClientAsync())
            await ServeConnectionAsync(second, mismatchedResponse: false);
    }

    private static async Task ServeOperationErrorAsync(TcpListener listener)
    {
        using var connection = await listener.AcceptTcpClientAsync();
        await using var stream = connection.GetStream();
        using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true)
        {
            AutoFlush = true
        };
        var request = JsonNode.Parse(await reader.ReadLineAsync() ?? throw new EndOfStreamException())!.AsObject();
        await writer.WriteAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            result = new object[]
            {
                new { error = "constraint violation", details = new object[] { "unexpected", "shape" } }
            },
            error = (object?)null,
            id = request["id"]!.GetValue<string>()
        }));
    }

    private static async Task ServeErrorAsync(TcpListener listener)
    {
        using var connection = await listener.AcceptTcpClientAsync();
        await using var stream = connection.GetStream();
        using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true)
        {
            AutoFlush = true
        };
        var request = JsonNode.Parse(await reader.ReadLineAsync() ?? throw new EndOfStreamException())!.AsObject();
        await writer.WriteAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            result = (object?)null,
            error = new { error = "constraint violation", details = "secret-table-detail" },
            id = request["id"]!.GetValue<string>()
        }));
    }

    private static async Task ServeEchoThenResponseAsync(TcpListener listener)
    {
        using var connection = await listener.AcceptTcpClientAsync();
        await using var stream = connection.GetStream();
        using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true)
        {
            AutoFlush = true
        };
        var request = JsonNode.Parse(await reader.ReadLineAsync() ?? throw new EndOfStreamException())!.AsObject();
        await writer.WriteAsync("{\"jsonrpc\":\"2.0\",\"method\":\"echo\",\"params\":[\"keepalive\"],\"id\":\"echo-1\"}");
        var echo = JsonNode.Parse(await reader.ReadLineAsync() ?? throw new EndOfStreamException())!.AsObject();
        Assert.Equal("echo-1", echo["id"]!.GetValue<string>());
        Assert.Equal("keepalive", echo["result"]![0]!.GetValue<string>());
        await writer.WriteAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", result = Array.Empty<object>(), error = (string?)null,
            id = request["id"]!.GetValue<string>()
        }));
    }

    private static async Task ServeConnectionAsync(TcpClient connection, bool mismatchedResponse)
    {
        await using var stream = connection.GetStream();
        using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true)
        {
            AutoFlush = true
        };
        var request = JsonNode.Parse(await reader.ReadLineAsync() ?? throw new EndOfStreamException())!.AsObject();
        var id = mismatchedResponse ? "mismatched" : request["id"]!.GetValue<string>();
        await writer.WriteAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", result = Array.Empty<object>(), error = (string?)null, id
        }));
    }
}
