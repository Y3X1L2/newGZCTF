using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Images;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public sealed class TeamLabModbusDeviceTests
{
    [Fact]
    public async Task ConfiguredDeviceSupportsRealStatefulProtocolAndInstanceIsolation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var token = timeout.Token;
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Modbus");
        var source = string.Join("", Directory.GetFiles(directory).Order().Select(File.ReadAllText));
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..12];
        var image = new ImageFromDockerfileBuilder().WithName("gzctf-qa-modbus:" + digest)
            .WithDockerfileDirectory(directory).WithImageBuildPolicy(PullPolicy.Missing)
            .WithCleanUp(false).WithDeleteIfExists(false).Build();
        await image.CreateAsync(token);
        var builder = new ContainerBuilder(image).WithPortBinding(1502, true).WithPortBinding(1503, true)
            .WithEnvironment("GZCTF_DEVICE_PARAMETERS", """{"unitId":7,"port":1502,"holdingRegisters":[12,34,56,78]}""")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(1502));
        await using var first = builder.Build();
        await using var second = builder.Build();
        await first.StartAsync(token);
        await second.StartAsync(token);
        using var http = new HttpClient();
        async Task<JsonElement> Telemetry() => JsonDocument.Parse(await http.GetStringAsync(
            $"http://{first.Hostname}:{first.GetMappedPublicPort(1503)}/health", token)).RootElement.Clone();
        var initialTelemetry = await Telemetry();
        Assert.Equal(0, initialTelemetry.GetProperty("counters").GetProperty("modbus.read").GetInt64());

        async Task<byte[]> Exchange(int port, byte[] pdu, bool fragmented = false)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            using var client = new TcpClient();
            await client.ConnectAsync(first.Hostname, port, deadline.Token);
            var stream = client.GetStream();
            var frame = new byte[7 + pdu.Length];
            BinaryPrimitives.WriteUInt16BigEndian(frame, 123);
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), (ushort)(1 + pdu.Length));
            frame[6] = 7;
            pdu.CopyTo(frame, 7);
            if (fragmented)
            {
                await stream.WriteAsync(frame.AsMemory(0, 4), deadline.Token);
                await Task.Delay(30, deadline.Token);
                await stream.WriteAsync(frame.AsMemory(4), deadline.Token);
            }
            else await stream.WriteAsync(frame, deadline.Token);
            var header = new byte[7];
            await stream.ReadExactlyAsync(header, deadline.Token);
            Assert.Equal(123, BinaryPrimitives.ReadUInt16BigEndian(header));
            Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2)));
            Assert.Equal(7, header[6]);
            var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4)) - 1;
            Assert.InRange(length, 1, 253);
            var response = new byte[length];
            await stream.ReadExactlyAsync(response, deadline.Token);
            return response;
        }

        var port = first.GetMappedPublicPort(1502);
        Assert.Equal(new byte[] { 3, 4, 0, 12, 0, 34 }, await Exchange(port, [3, 0, 0, 0, 2], true));
        Assert.Equal(new byte[] { 6, 0, 0, 0xbe, 0xef }, await Exchange(port, [6, 0, 0, 0xbe, 0xef]));
        Assert.Equal(new byte[] { 3, 2, 0xbe, 0xef }, await Exchange(port, [3, 0, 0, 0, 1]));
        Assert.Equal(new byte[] { 16, 0, 1, 0, 2 }, await Exchange(port, [16, 0, 1, 0, 2, 4, 0, 99, 0, 100]));
        Assert.Equal(new byte[] { 3, 6, 0xbe, 0xef, 0, 99, 0, 100 }, await Exchange(port, [3, 0, 0, 0, 3]));
        Assert.Equal(new byte[] { 0x83, 2 }, await Exchange(port, [3, 0, 4, 0, 1]));
        Assert.Equal(new byte[] { 0x83, 3 }, await Exchange(port, [3, 0, 0, 0, 0]));
        Assert.Equal(new byte[] { 5, 0, 0, 0xff, 0 }, await Exchange(port, [5, 0, 0, 0xff, 0]));
        Assert.Equal(new byte[] { 1, 1, 1 }, await Exchange(port, [1, 0, 0, 0, 1]));
        Assert.Equal(new byte[] { 0x85, 3 }, await Exchange(port, [5, 0, 0, 0, 1]));
        Assert.Equal(new byte[] { 1, 1, 1 }, await Exchange(port, [1, 0, 0, 0, 1]));
        Assert.Equal(new byte[] { 0x90, 3 }, await Exchange(port, [16, 0, 0, 0, 0, 0]));
        var telemetry = await Telemetry();
        Assert.Equal(3, telemetry.GetProperty("counters").GetProperty("modbus.write").GetInt64());
        Assert.Equal(5, telemetry.GetProperty("counters").GetProperty("modbus.read").GetInt64());
        Assert.Equal(new byte[] { 3, 2, 0, 12 }, await Exchange(second.GetMappedPublicPort(1502), [3, 0, 0, 0, 1]));
        await first.StopAsync(token);
        await first.StartAsync(token);
        var restartedTelemetry = await Telemetry();
        Assert.NotEqual(initialTelemetry.GetProperty("bootId").GetString(), restartedTelemetry.GetProperty("bootId").GetString());
        Assert.Equal(0, restartedTelemetry.GetProperty("counters").GetProperty("modbus.read").GetInt64());
        Assert.Equal(new byte[] { 3, 2, 0, 12 }, await Exchange(first.GetMappedPublicPort(1502), [3, 0, 0, 0, 1]));
    }
}
