using System.Net;
using System.Net.Sockets;

namespace GZCTF.Agent.Services.TeamLab;

/// <summary>
/// A minimal network-namespace probe invoked by the Agent through nsenter.
/// It deliberately does not depend on utilities installed in the workload.
/// </summary>
public static class AgentNetworkProbe
{
    const string Mode = "--teamlab-network-probe";

    public static bool IsInvocation(string[] args) => args.Length > 0 && args[0] == Mode;

    public static async Task<int> RunAsync(string[] args, CancellationToken token)
    {
        if (args.Length is < 4 or > 5 ||
            args[1] is not ("tcp" or "http") ||
            !IPAddress.TryParse(args[2], out _) ||
            !int.TryParse(args[3], out var port) || port is < 1 or > 65535)
            return 64;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(args[2], port, timeout.Token);
            if (args[1] == "tcp") return 0;

            var path = args.Length == 5 ? args[4] : "/";
            if (!path.StartsWith('/') || path.Any(char.IsControl)) return 64;
            await using var stream = client.GetStream();
            var request = System.Text.Encoding.ASCII.GetBytes(
                $"GET {path} HTTP/1.0\\r\\nHost: {args[2]}\\r\\nConnection: close\\r\\n\\r\\n");
            await stream.WriteAsync(request, timeout.Token);
            var buffer = new byte[64];
            var read = await stream.ReadAsync(buffer, timeout.Token);
            var response = System.Text.Encoding.ASCII.GetString(buffer, 0, read);
            return response.StartsWith("HTTP/") && response.Length >= 12 && response[9] is '2' or '3' ? 0 : 1;
        }
        catch (Exception exception) when (exception is SocketException or IOException or OperationCanceledException)
        {
            return 1;
        }
    }
}
