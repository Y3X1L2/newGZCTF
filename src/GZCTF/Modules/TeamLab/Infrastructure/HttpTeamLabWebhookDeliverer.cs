using System.Text;
using System.Net;
using System.Net.Sockets;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.Extensions.Options;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class HttpTeamLabWebhookDeliverer(
    ILogger<HttpTeamLabWebhookDeliverer> logger)
    : ITeamLabWebhookDeliverer
{
    public async Task<TeamLabWebhookDeliveryResult> DeliverAsync(
        TeamLabWebhookSubscriptionView subscription,
        TeamLabWebhookEventEnvelope envelope,
        string body,
        string signature,
        CancellationToken cancellationToken)
    {
        try
        {
            var endpoint = await TeamLabWebhookEndpointValidator.TryResolveForDeliveryAsync(
                subscription.EndpointUrl, cancellationToken);
            if (endpoint is null)
                return new TeamLabWebhookDeliveryResult(false, TeamLabWebhookErrorCodes.EndpointUnreachable);
            using var request = new HttpRequestMessage(HttpMethod.Post, subscription.EndpointUrl)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-TeamLab-Event-Id", envelope.Id);
            request.Headers.Add("X-TeamLab-Event-Type", envelope.Type);
            request.Headers.Add("X-TeamLab-Timestamp",
                envelope.OccurredAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
            request.Headers.Add("X-TeamLab-Signature", signature);
            using var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                ConnectCallback = (context, token) => ConnectPinnedAsync(endpoint.Addresses, context.DnsEndPoint.Port, token)
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!TeamLabWebhookDelivery.IsSuccess(response.StatusCode))
                return new TeamLabWebhookDeliveryResult(false,
                    $"HTTP {(int)response.StatusCode}");
            return new TeamLabWebhookDeliveryResult(true, string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "TeamLab webhook 投递失败：{SubscriptionId}", subscription.Id);
            return new TeamLabWebhookDeliveryResult(false,
                "delivery_failed");
        }
    }

    internal static async ValueTask<Stream> ConnectPinnedAsync(
        IReadOnlyList<IPAddress> addresses,
        int port,
        CancellationToken cancellationToken)
    {
        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception exception) when (exception is SocketException or IOException)
            {
                last = exception;
                socket.Dispose();
            }
        }
        throw last ?? new SocketException((int)SocketError.HostUnreachable);
    }
}
