using System.Text.Json;
using GZCTF.GuestControl.Contracts;

namespace GZCTF.GuestSupervisor;

public sealed record GuestSupervisorConfiguration(
    int SchemaVersion,
    GuestAssetIdentity Identity,
    Uri EnrollmentEndpoint,
    string EnrollmentToken,
    string WorkerServerCertificateSha256,
    string IntentDigest,
    string StateRoot,
    IReadOnlyList<GuestNetworkExpectation>? NetworkInterfaces = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string DefaultPath => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "GZCTF", "GuestSupervisor", "config.json")
        : "/etc/gzctf/guest-supervisor/config.json";

    public static async Task<GuestSupervisorConfiguration> LoadAsync(
        string? path,
        CancellationToken cancellationToken)
    {
        path = string.IsNullOrWhiteSpace(path) ? DefaultPath : Path.GetFullPath(path);
        await using var stream = File.OpenRead(path);
        var value = await JsonSerializer.DeserializeAsync<GuestSupervisorConfiguration>(
            stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("guest_supervisor_configuration_invalid");
        if (value.SchemaVersion != GuestControlProtocol.SchemaVersion ||
            value.Identity.RuntimeId <= 0 || value.Identity.Generation <= 0 ||
            value.EnrollmentEndpoint.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(value.EnrollmentToken) ||
            value.WorkerServerCertificateSha256.Length != 64 ||
            string.IsNullOrWhiteSpace(value.IntentDigest))
            throw new InvalidDataException("guest_supervisor_configuration_invalid");
        return value;
    }
}
