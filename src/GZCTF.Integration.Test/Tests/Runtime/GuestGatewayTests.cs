using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GZCTF.Agent.Controllers;
using GZCTF.Agent.Middlewares;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using GZCTF.Agent.Services.GuestControl;
using GZCTF.Agent.Services.RuntimeSignals;
using GZCTF.GuestControl.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Runtime;

public sealed class GuestGatewayTests
{
    [Fact]
    public async Task GuestGateway_EnrollsThenAcceptsMtlsLifecycleEvent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gzctf-guest-gateway-{Guid.NewGuid():N}");
        var port = FreePort();
        var agentConfig = new AgentConfig
        {
            AuthToken = "platform-token",
            ListenPort = 5001,
            GuestManagement = new GuestManagementConfig
            {
                Enabled = true,
                HostAddress = "127.0.0.1",
                ListenPort = port,
                StateRoot = root
            }
        };
        var authority = new GuestCertificateAuthority(Options.Create(agentConfig));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenLocalhost(port, listener =>
            listener.UseHttps(https =>
            {
                https.ServerCertificate = authority.GetServerCertificate();
                https.ServerCertificateChain = new X509Certificate2Collection(
                    authority.GetAuthorityCertificate());
                https.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
                https.ClientCertificateValidation = static (_, _, _) => true;
            })));
        builder.Services.AddSingleton(Options.Create(agentConfig));
        builder.Services.AddSingleton(Options.Create(new AgentTeamLabConfig { RuntimeStateRoot = root }));
        builder.Services.AddSingleton<AgentResourceLock>();
        builder.Services.AddSingleton(authority);
        builder.Services.AddSingleton<GuestEnrollmentStore>();
        builder.Services.AddSingleton<AgentRuntimeSignalJournal>();
        builder.Services.AddSingleton<AgentRuntimeSignalPublisher>();
        builder.Services.AddSingleton<GuestEventIngestor>();
        builder.Services.AddHttpClient();
        builder.Services.AddControllers().AddApplicationPart(typeof(GuestGatewayController).Assembly);
        await using var app = builder.Build();
        app.UseMiddleware<AgentCorrelationErrorMiddleware>();
        app.UseMiddleware<AgentEndpointAuthenticationMiddleware>();
        app.MapControllers();

        try
        {
            await app.StartAsync();
            var identity = new GuestAssetIdentity(
                Guid.CreateVersion7(), 52, 2, "windows", "tl52-windows", Guid.CreateVersion7(), 0);
            var intent = new GuestBootstrapIntent(
                GuestControlProtocol.SchemaVersion,
                GuestControlProtocol.SchemaVersion,
                identity,
                "sha256:gateway-intent",
                "sha256:gateway-image",
                null,
                DateTimeOffset.UtcNow.AddMinutes(5));
            var store = app.Services.GetRequiredService<GuestEnrollmentStore>();
            var prepared = await store.PrepareAsync(
                new GuestControlPrepareRequest(identity, intent, DateTimeOffset.UtcNow.AddMinutes(5)),
                authority.GetAuthoritySha256(),
                CancellationToken.None);
            using var guestKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var csr = new CertificateRequest("CN=fixture", guestKey, HashAlgorithmName.SHA256)
                .CreateSigningRequestPem();
            var envelope = new GuestEnrollmentEnvelope(
                prepared.EnrollmentToken,
                new GuestEnrollmentRequest(
                    GuestControlProtocol.SchemaVersion,
                    identity,
                    csr,
                    GuestControlProtocol.CsrAlgorithm,
                    intent.IntentDigest,
                    DateTimeOffset.UtcNow));
            using var anonymousHandler = Handler();
            using var anonymousClient = new HttpClient(anonymousHandler)
            {
                BaseAddress = new Uri($"https://127.0.0.1:{port}")
            };
            using var enrollmentResponse = await anonymousClient.PostAsJsonAsync(
                "/api/guest/v1/enroll", envelope);
            Assert.True(enrollmentResponse.IsSuccessStatusCode,
                await enrollmentResponse.Content.ReadAsStringAsync());
            var session = await enrollmentResponse.Content.ReadFromJsonAsync<GuestEnrollmentSessionResponse>();
            Assert.NotNull(session);

            using var transientClientCertificate = X509Certificate2.CreateFromPem(
                session!.Enrollment.ClientCertificatePem,
                guestKey.ExportPkcs8PrivateKeyPem());
            using var clientCertificate = X509CertificateLoader.LoadPkcs12(
                transientClientCertificate.Export(X509ContentType.Pkcs12), null,
                OperatingSystem.IsWindows()
                    ? X509KeyStorageFlags.Exportable | X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet
                    : X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
            using var clientHandler = Handler(clientCertificate);
            using var client = new HttpClient(clientHandler)
            {
                BaseAddress = anonymousClient.BaseAddress
            };
            var guestEvent = new GuestLifecycleEvent(
                GuestControlProtocol.SchemaVersion,
                identity,
                1,
                GuestLifecycleStage.ManagementLinkReady,
                GuestLifecycleOutcome.Ready,
                DateTimeOffset.UtcNow,
                "sha256:gateway-event",
                Facts: new Dictionary<string, string>
                {
                    ["failedStep"] = "install",
                    ["failureCategory"] = "completed"
                });
            using var eventResponse = await client.PostAsJsonAsync(
                "/api/guest/v1/events", new GuestEventEnvelope(guestEvent));
            eventResponse.EnsureSuccessStatusCode();
            Assert.Equal(GuestEventDisposition.Accepted,
                await eventResponse.Content.ReadFromJsonAsync<GuestEventDisposition>());
            var journal = app.Services.GetRequiredService<AgentRuntimeSignalJournal>();
            var signal = Assert.Single(await journal.ReadAllAsync(identity.OperationId, CancellationToken.None));
            Assert.Equal(AgentRuntimeSignalStage.ManagementLinkReady, signal.Stage);
            Assert.Equal(identity.NativeVmId.ToString("D"), signal.Facts!["nativeVmId"]);
            Assert.Equal("install", signal.Facts["failedStep"]);
            Assert.Equal("completed", signal.Facts["failureCategory"]);
        }
        finally
        {
            await app.StopAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static HttpClientHandler Handler(X509Certificate2? certificate = null)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        if (certificate is not null) handler.ClientCertificates.Add(certificate);
        return handler;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
