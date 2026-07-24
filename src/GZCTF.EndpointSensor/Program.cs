using GZCTF.GuestTelemetry.Contracts;
using GZCTF.GuestTelemetry.Platform;
using GZCTF.EndpointSensor.Security;
using GZCTF.EndpointSensor.Transport;

var runtimePublicId = Required("GZCTF_SENSOR_RUNTIME_PUBLIC_ID");
var generation = int.Parse(Required("GZCTF_SENSOR_GENERATION"));
var assetKey = Required("GZCTF_SENSOR_ASSET_KEY");
var channel = Required("GZCTF_SENSOR_CHANNEL");
var key = Convert.FromBase64String(Required("GZCTF_SENSOR_HMAC"));
var signer = new SensorEventSigner(key);
IConnectionProvider provider = OperatingSystem.IsWindows()
    ? new WindowsConnectionProvider()
    : new LinuxConnectionProvider();
await using var writer = new SensorChannelWriter(channel);
var previous = new Dictionary<string, ConnectionSnapshot>(StringComparer.Ordinal);
var sequence = checked(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000);
using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};
using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
while (await timer.WaitForNextTickAsync(shutdown.Token))
{
    var observedAt = DateTimeOffset.UtcNow;
    var current = (await provider.ReadAsync(shutdown.Token))
        .Take(20_000)
        .ToDictionary(item => item.Identity, StringComparer.Ordinal);
    foreach (var connection in current.Values.OrderBy(item => item.Identity, StringComparer.Ordinal))
    {
        var kind = previous.ContainsKey(connection.Identity)
            ? SensorEventKind.Observed
            : SensorEventKind.Opened;
        await writer.WriteAsync(signer.Sign(new SensorEvent(
            1,
            runtimePublicId,
            generation,
            assetKey,
            ++sequence,
            observedAt,
            kind,
            connection.Process,
            connection.Local,
            connection.Remote,
            string.Empty)), shutdown.Token);
    }
    foreach (var connection in previous.Where(item => !current.ContainsKey(item.Key))
                 .Select(item => item.Value)
                 .OrderBy(item => item.Identity, StringComparer.Ordinal))
        await writer.WriteAsync(signer.Sign(new SensorEvent(
            1,
            runtimePublicId,
            generation,
            assetKey,
            ++sequence,
            observedAt,
            SensorEventKind.Closed,
            connection.Process,
            connection.Local,
            connection.Remote,
            string.Empty)), shutdown.Token);
    previous = current;
}

static string Required(string key)
{
    var value = Environment.GetEnvironmentVariable(key);
    return !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException($"Required sensor setting '{key}' is missing.");
}
