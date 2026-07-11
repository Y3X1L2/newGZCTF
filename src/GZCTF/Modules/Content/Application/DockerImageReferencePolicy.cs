using System.Net;
using System.Net.Sockets;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Application;

namespace GZCTF.Modules.Content.Application;

public sealed class DockerImageReferencePolicy
{
    private const string InternalRegistry = DockerRegistrySettings.FixedAddress;

    public async Task ValidateAsync(string imageReference, CancellationToken cancellationToken)
    {
        var registry = ExtractRegistry(imageReference);
        if (registry is null || string.Equals(
                registry,
                InternalRegistry,
                StringComparison.OrdinalIgnoreCase))
            return;

        var host = ExtractHost(registry);
        if (IPAddress.TryParse(host, out var address))
        {
            RejectNonPublic(address);
            return;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        }
        catch (SocketException)
        {
            throw new ImageReferencePolicyException(
                "image_reference_unresolvable",
                "The Docker registry host could not be resolved.");
        }

        if (addresses.Length == 0)
            throw new ImageReferencePolicyException(
                "image_reference_unresolvable",
                "The Docker registry host could not be resolved.");
        foreach (var resolved in addresses)
            RejectNonPublic(resolved);
    }

    private static string? ExtractRegistry(string imageReference)
    {
        var value = imageReference.Trim();
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                !string.IsNullOrEmpty(uri.UserInfo))
                throw Forbidden();
            value = uri.Authority + uri.AbsolutePath;
        }

        if (value.Any(char.IsWhiteSpace))
            throw Forbidden();

        var slashIndex = value.IndexOf('/');
        if (slashIndex < 0)
            return null;
        var firstSegment = value[..slashIndex];
        if (firstSegment.Length == 0)
            throw Forbidden();
        return firstSegment.Contains('.') || firstSegment.Contains(':') ||
               string.Equals(firstSegment, "localhost", StringComparison.OrdinalIgnoreCase)
            ? firstSegment
            : null;
    }

    private static string ExtractHost(string registry)
    {
        if (!Uri.TryCreate($"http://{registry}", UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            throw Forbidden();
        return uri.Host;
    }

    private static void RejectNonPublic(IPAddress address)
    {
        if (!IsPublic(address))
            throw Forbidden();
    }

    private static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var first = bytes[0];
            var second = bytes[1];
            return first is not 0 and not 10 and not 127 &&
                   !(first == 100 && second is >= 64 and <= 127) &&
                   !(first == 169 && second == 254) &&
                   !(first == 172 && second is >= 16 and <= 31) &&
                   !(first == 192 && second == 0 && bytes[2] is 0 or 2) &&
                   !(first == 192 && second == 168) &&
                   !(first == 198 && second is 18 or 19) &&
                   !(first == 198 && second == 51 && bytes[2] == 100) &&
                   !(first == 203 && second == 0 && bytes[2] == 113) &&
                   first < 224;
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6 ||
            address.Equals(IPAddress.IPv6Loopback) || address.Equals(IPAddress.IPv6None))
            return false;
        return (bytes[0] & 0xfe) != 0xfc &&
               !(bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80) &&
               bytes[0] != 0xff &&
               !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8);
    }

    private static ImageReferencePolicyException Forbidden() => new(
        "image_reference_forbidden",
        "The Docker image reference must use the platform registry or a public registry without credentials.");
}

public sealed class ImageReferencePolicyException(
    string code,
    string message)
    : ApiContractException(code, message, 422)
;
