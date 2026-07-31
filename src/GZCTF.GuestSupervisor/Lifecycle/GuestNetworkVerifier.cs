using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using GZCTF.GuestControl.Contracts;

namespace GZCTF.GuestSupervisor.Lifecycle;

public sealed class GuestNetworkVerifier(GuestSupervisorConfiguration configuration)
{
    public Task VerifyAsync(CancellationToken cancellationToken)
    {
        var expected = configuration.NetworkInterfaces ?? [];
        if (expected.Count == 0) throw new InvalidDataException("guest_network_expectation_missing");
        var actual = NetworkInterface.GetAllNetworkInterfaces()
            .Where(item => item.OperationalStatus == OperationalStatus.Up && item.GetPhysicalAddress().GetAddressBytes().Length == 6)
            .ToDictionary(
                item => NormalizeMac(item.GetPhysicalAddress().ToString()),
                item => item.GetIPProperties().UnicastAddresses,
                StringComparer.OrdinalIgnoreCase);
        foreach (var item in expected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!actual.TryGetValue(NormalizeMac(item.MacAddress), out var addresses) ||
                !IPAddress.TryParse(item.IpAddress, out var expectedAddress) ||
                expectedAddress.AddressFamily != AddressFamily.InterNetwork ||
                !addresses.Any(address => address.Address.Equals(expectedAddress) &&
                                          PrefixLength(address) == item.PrefixLength))
                throw new InvalidOperationException(
                    item.IsManagement ? "guest_management_network_not_applied" : "guest_topology_network_not_applied");
        }
        return Task.CompletedTask;
    }

    private static int PrefixLength(UnicastIPAddressInformation address)
    {
        if (address.PrefixLength is >= 0 and <= 32) return address.PrefixLength;
        var bytes = address.IPv4Mask.GetAddressBytes();
        return bytes.Sum(value => System.Numerics.BitOperations.PopCount(value));
    }

    private static string NormalizeMac(string value) =>
        new(value.Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());
}
