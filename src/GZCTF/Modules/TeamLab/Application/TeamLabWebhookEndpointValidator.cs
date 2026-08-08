using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using GZCTF.Modules.TeamLab.Contracts;

namespace GZCTF.Modules.TeamLab.Application;

/// <summary>
/// Webhook endpoint SSRF guard. HTTPS only; the resolved endpoint must not be a
/// loopback, private, link-local, multicast, reserved or platform-host address.
/// DNS is re-validated before every delivery batch, not only at creation.
/// </summary>
public static class TeamLabWebhookEndpointValidator
{
    internal sealed record ValidatedEndpoint(Uri Uri, IReadOnlyList<IPAddress> Addresses);

    public static async Task<string> ValidateAndNormalizeAsync(
        string endpoint,
        CancellationToken cancellationToken)
    {
        var validated = await ResolveValidatedAsync(endpoint, cancellationToken);
        return validated.Uri.AbsoluteUri;
    }

    internal static async Task<ValidatedEndpoint?> TryResolveForDeliveryAsync(
        string endpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ResolveValidatedAsync(endpoint, cancellationToken);
        }
        catch (TeamLabApiContractException)
        {
            return null;
        }
    }

    private static async Task<ValidatedEndpoint> ResolveValidatedAsync(
        string endpoint,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host) || uri.Port is < 1 or > 65535 ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw Invalid("Webhook 端点必须是有效的 https URL，且不能包含用户信息。");
        var addresses = await ResolveAsync(uri.IdnHost, cancellationToken);
        if (addresses.Count == 0)
            throw Invalid("Webhook 端点域名无法解析。");
        foreach (var address in addresses)
        {
            if (IsForbidden(address) || await IsPlatformHostAsync(address, cancellationToken))
                throw Invalid("Webhook 端点不能指向内网、回环、链路本地、组播或平台主机地址。");
        }
        return new ValidatedEndpoint(uri, addresses);
    }

    internal static bool IsForbidden(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return true;
        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            var first = bytes[0];
            var second = bytes[1];
            if (first is 0 or 10 or 127) return true;
            if (first == 172 && second is >= 16 and <= 31) return true;
            if (first == 192 && second == 168) return true;
            if (first == 169 && second == 254) return true;
            if (first == 100 && second is >= 64 and <= 127) return true;
            if (first == 192 && second is 0 or 88 or 18) return true;
            if (first == 198 && second is >= 18 and <= 19) return true;
            if (first is >= 224 and <= 239) return true;
            if (first >= 240) return true;
            return false;
        }
        if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
            return true;
        if (bytes.Length == 16)
        {
            if ((bytes[0] & 0xfe) == 0xfc) return true;
            if (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0x00 && bytes[3] == 0x00) return true;
            if (bytes[0] == 0xfe && bytes[1] == 0xc0) return true;
            if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8) return true;
        }
        return false;
    }

    internal static async Task<IReadOnlyList<IPAddress>> ResolveAsync(
        string host,
        CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal))
            return [literal];
        try
        {
            return await Dns.GetHostAddressesAsync(host, cancellationToken);
        }
        catch (SocketException)
        {
            return [];
        }
        catch (ArgumentException)
        {
            return [];
        }
    }

    internal static async Task<bool> IsPlatformHostAsync(IPAddress address, CancellationToken cancellationToken)
    {
        try
        {
            var interfaces = await Task.Run(() =>
                NetworkInterface.GetAllNetworkInterfaces()
                    .SelectMany(item => item.GetIPProperties().UnicastAddresses)
                    .Select(item => item.Address)
                    .ToArray(), cancellationToken);
            return interfaces.Any(local => local.Equals(address));
        }
        catch (Exception)
        {
            // Fail closed: if the local interface inventory cannot be enumerated,
            // treat the address as a platform host rather than silently allowing it.
            return true;
        }
    }

    private static TeamLabApiContractException Invalid(string message) =>
        new(TeamLabWebhookErrorCodes.EndpointInvalid, message, 422);
}
