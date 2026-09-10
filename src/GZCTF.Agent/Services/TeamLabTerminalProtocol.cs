using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace GZCTF.Agent.Services;

// Binary messages carry PTY bytes. Text messages carry bounded JSON control commands.
internal static class TeamLabTerminalProtocol
{
    internal const int MaxMessageBytes = 65536;

    internal static async Task CopyInputAsync(WebSocket socket,
        Func<byte[], CancellationToken, Task> write,
        Func<int, int, CancellationToken, Task> resize,
        CancellationToken token)
    {
        var buffer = new byte[8192];
        while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                if (result.MessageType == WebSocketMessageType.Close) return;
                if (message.Length + result.Count > MaxMessageBytes)
                    throw new InvalidDataException("Terminal message exceeds the size limit.");
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            var bytes = message.ToArray();
            if (result.MessageType == WebSocketMessageType.Binary)
            {
                await write(bytes, token);
                continue;
            }
            await ApplyControlAsync(bytes, write, resize, token);
        }
    }

    internal static async Task ApplyControlAsync(byte[] bytes,
        Func<byte[], CancellationToken, Task> write,
        Func<int, int, CancellationToken, Task> resize,
        CancellationToken token)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        var root = document.RootElement;
        switch (root.GetProperty("type").GetString())
        {
            case "resize":
                var cols = root.GetProperty("cols").GetInt32();
                var rows = root.GetProperty("rows").GetInt32();
                if (cols is < 2 or > 500 || rows is < 1 or > 300)
                    throw new InvalidDataException("Invalid terminal dimensions.");
                await resize(cols, rows, token);
                break;
            case "input":
                await write(Encoding.UTF8.GetBytes(root.GetProperty("data").GetString() ?? ""), token);
                break;
            case "signal":
                var signal = root.GetProperty("signal").GetString() switch
                {
                    "INT" => (byte)3,
                    "EOF" => (byte)4,
                    "TSTP" => (byte)26,
                    _ => throw new InvalidDataException("Unsupported terminal signal.")
                };
                await write([signal], token);
                break;
            default:
                throw new InvalidDataException("Unsupported terminal command.");
        }
    }
}
