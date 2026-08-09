using System.Net.Sockets;
using System.Text.Json;
using GZCTF.GuestTelemetry.Contracts;
using GZCTF.EndpointSensor.Serialization;

namespace GZCTF.EndpointSensor.Transport;

public sealed class SensorChannelWriter(string endpoint) : IAsyncDisposable
{
    private Stream? _stream;

    public async Task WriteAsync(SensorEvent value, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, SensorJsonContext.Default.SensorEvent);
        if (payload.Length > 16 * 1024)
            throw new InvalidOperationException("Sensor event exceeds the transport limit.");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                _stream ??= await ConnectAsync(cancellationToken);
                await _stream.WriteAsync(payload, cancellationToken);
                await _stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
                await _stream.FlushAsync(cancellationToken);
                return;
            }
            catch (Exception exception) when (
                attempt == 0 && exception is IOException or SocketException or UnauthorizedAccessException)
            {
                await ResetAsync();
            }
        }
    }

    public async ValueTask DisposeAsync() => await ResetAsync();

    private async Task<Stream> ConnectAsync(CancellationToken cancellationToken)
    {
        if (endpoint.StartsWith("unix://", StringComparison.OrdinalIgnoreCase))
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint[7..]), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        return new FileStream(
            endpoint,
            FileMode.Open,
            FileAccess.Write,
            FileShare.ReadWrite,
            4096,
            FileOptions.Asynchronous);
    }

    private async Task ResetAsync()
    {
        if (_stream is not null) await _stream.DisposeAsync();
        _stream = null;
    }
}
