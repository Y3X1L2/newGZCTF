using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Agent.Middlewares;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using GZCTF.Agent.Services.GuestControl;
using GZCTF.Agent.Services.TeamLab;
using GZCTF.Agent.Services.Vm;
using GZCTF.GuestControl.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GZCTF.Test.UnitTests.Runtime;

public sealed class GuestManagementControlPlaneTests
{
    [Fact]
    public void GuestManagementNetwork_PlanIsIsolatedAndIdempotent()
    {
        var options = Options.Create(new AgentConfig
        {
            GuestManagement = new GuestManagementConfig { Enabled = true }
        });
        var runner = new TeamLabCommandRunner(NullLogger<TeamLabCommandRunner>.Instance);
        var executor = new TeamLabCommandExecutor(
            Options.Create(new AgentTeamLabConfig { Enable = true, DryRun = true }),
            runner,
            NullLogger<TeamLabCommandExecutor>.Instance);
        var service = new GuestManagementNetworkService(
            options, executor, runner, new AgentResourceLock());

        var plan = service.BuildPlan();

        Assert.Contains(plan, command => command.Contains("ip address replace '100.127.0.1/16'", StringComparison.Ordinal));
        var nft = Assert.Single(plan,
            command => command.Contains("gzctf_guest_mgmt", StringComparison.Ordinal));
        Assert.Contains("nft list table inet gzctf_guest_mgmt", nft, StringComparison.Ordinal);
        Assert.Contains("nft delete table inet gzctf_guest_mgmt", nft, StringComparison.Ordinal);
        Assert.DoesNotContain("destroy table inet gzctf_guest_mgmt", nft, StringComparison.Ordinal);
        Assert.Contains("tcp dport 5443 accept", nft, StringComparison.Ordinal);
        Assert.Contains("iifname \"gzmgt0\" drop", nft, StringComparison.Ordinal);
        Assert.Contains("oifname \"gzmgt0\" drop", nft, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GuestEnrollment_UsesEncryptedOneTimeStateAndJournalsBeforeAcknowledge()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gzctf-guest-control-{Guid.NewGuid():N}");
        try
        {
            var options = Options.Create(new AgentConfig
            {
                GuestManagement = new GuestManagementConfig
                {
                    Enabled = true,
                    StateRoot = root
                }
            });
            var store = new GuestEnrollmentStore(options, new AgentResourceLock());
            var authority = new GuestCertificateAuthority(options);
            var identity = Identity();
            var intent = new GuestBootstrapIntent(
                GuestControlProtocol.SchemaVersion,
                GuestControlProtocol.SchemaVersion,
                identity,
                "sha256:intent-fixture",
                "sha256:prepared-fixture",
                null,
                DateTimeOffset.UtcNow.AddMinutes(10),
                SecretReferences:
                [
                    new GuestSecretReference("flag", "secret:flag", "/opt/gzctf/runtime/flag")
                ]);
            var prepared = await store.PrepareAsync(
                new GuestControlPrepareRequest(
                    identity, intent, DateTimeOffset.UtcNow.AddMinutes(10),
                    new Dictionary<string, string> { ["secret:flag"] = "flag{never-log-this}" }),
                authority.GetAuthoritySha256(),
                CancellationToken.None);
            var stateJson = File.ReadAllText(
                Directory.EnumerateFiles(root, "*.guest.json", SearchOption.AllDirectories).Single());
            Assert.DoesNotContain(prepared.EnrollmentToken, stateJson, StringComparison.Ordinal);
            Assert.DoesNotContain("prepared-fixture", stateJson, StringComparison.Ordinal);
            Assert.DoesNotContain("never-log-this", stateJson, StringComparison.Ordinal);

            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var csr = new CertificateRequest("CN=untrusted-guest-subject", key, HashAlgorithmName.SHA256)
                .CreateSigningRequestPem();
            var enrollment = new GuestEnrollmentRequest(
                GuestControlProtocol.SchemaVersion,
                identity,
                csr,
                GuestControlProtocol.CsrAlgorithm,
                intent.IntentDigest,
                DateTimeOffset.UtcNow);
            var completed = await store.EnrollAsync(
                new GuestEnrollmentEnvelope(prepared.EnrollmentToken, enrollment),
                authority.IssueClientCertificate,
                CancellationToken.None);
            using var certificate = X509Certificate2.CreateFromPem(completed.Response.ClientCertificatePem);
            Assert.StartsWith("CN=gzctf-", certificate.Subject, StringComparison.Ordinal);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.EnrollAsync(
                new GuestEnrollmentEnvelope(prepared.EnrollmentToken, enrollment),
                authority.IssueClientCertificate,
                CancellationToken.None));

            var journaled = 0;
            var guestEvent = new GuestLifecycleEvent(
                GuestControlProtocol.SchemaVersion,
                identity,
                1,
                GuestLifecycleStage.ManagementLinkReady,
                GuestLifecycleOutcome.Ready,
                DateTimeOffset.UtcNow,
                "sha256:event-1");
            var accepted = await store.AcceptEventAsync(
                certificate.Thumbprint,
                guestEvent,
                _ =>
                {
                    journaled++;
                    return Task.CompletedTask;
                },
                CancellationToken.None);
            var duplicate = await store.AcceptEventAsync(
                certificate.Thumbprint,
                guestEvent,
                _ => throw new InvalidOperationException("duplicate must not be journaled"),
                CancellationToken.None);

            Assert.Equal(GuestEventDisposition.Accepted, accepted);
            Assert.Equal(GuestEventDisposition.Duplicate, duplicate);
            Assert.Equal(1, journaled);

            var enrolled = guestEvent with
            {
                Sequence = 2,
                Stage = GuestLifecycleStage.GuestEnrolled,
                PayloadDigest = "sha256:event-2"
            };
            await store.AcceptEventAsync(
                certificate.Thumbprint, enrolled, _ => Task.CompletedTask, CancellationToken.None);
            var networkApplied = enrolled with
            {
                Sequence = 3,
                Stage = GuestLifecycleStage.NetworkApplied,
                PayloadDigest = "sha256:event-3"
            };
            await store.AcceptEventAsync(
                certificate.Thumbprint, networkApplied, _ => Task.CompletedTask, CancellationToken.None);
            var bootstrapRunning = networkApplied with
            {
                Sequence = 4,
                Stage = GuestLifecycleStage.BootstrapRunning,
                PayloadDigest = "sha256:event-4"
            };
            await store.AcceptEventAsync(
                certificate.Thumbprint, bootstrapRunning, _ => Task.CompletedTask, CancellationToken.None);
            var rebootRequested = bootstrapRunning with
            {
                Sequence = 5,
                Stage = GuestLifecycleStage.RebootRequested,
                PayloadDigest = "sha256:event-5"
            };
            await store.AcceptEventAsync(
                certificate.Thumbprint, rebootRequested, _ => Task.CompletedTask, CancellationToken.None);
            var afterBootIdentity = identity with { BootEpoch = 1 };
            var afterBoot = rebootRequested with
            {
                Identity = afterBootIdentity,
                Sequence = 6,
                Stage = GuestLifecycleStage.GuestReenrolledAfterBoot,
                PayloadDigest = "sha256:event-6"
            };
            await store.AcceptEventAsync(
                certificate.Thumbprint, afterBoot, _ => Task.CompletedTask, CancellationToken.None);
            var secrets = await store.GetSecretsAsync(
                certificate.Thumbprint,
                new GuestSecretRequest(afterBootIdentity, ["secret:flag"]),
                CancellationToken.None);
            Assert.Equal("flag{never-log-this}", Assert.Single(secrets.Secrets).Value);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.GetSecretsAsync(
                certificate.Thumbprint,
                new GuestSecretRequest(afterBootIdentity, ["secret:not-declared"]),
                CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void VmDomainBuilder_AttachesManagementNicWithoutChangingTopologyInterfaces()
    {
        var request = new CreateVmRequest
        {
            VmName = "tl42-ad-dc",
            Generation = 3,
            Interfaces =
            [
                new VmNetworkInterfaceRequest
                {
                    BridgeName = "tl42-entry",
                    MacAddress = "02:42:00:00:00:10",
                    Model = "e1000e",
                    IpAddress = "10.20.0.10",
                    PrefixLength = 24
                }
            ],
            ManagementInterface = new VmManagementInterfaceConfig
            {
                BridgeName = "gzmgt0",
                MacAddress = "02:7f:00:00:00:10",
                IpAddress = "100.127.0.10"
            }
        };

        var arguments = KvmService.BuildVirtInstallNetworkArguments(request);
        var networkConfig = KvmService.BuildCloudInitNetworkConfig(request);
        var isolation = KvmService.BuildManagementPortIsolationCommand(request);

        Assert.Contains("bridge=tl42-entry", arguments, StringComparison.Ordinal);
        Assert.Contains("bridge=gzmgt0,model=e1000e,mac=02:7f:00:00:00:10", arguments, StringComparison.Ordinal);
        Assert.Contains("set-name: gzmgmt0", networkConfig, StringComparison.Ordinal);
        Assert.DoesNotContain("gateway4", networkConfig, StringComparison.Ordinal);
        Assert.Contains("bridge link set dev \"$tap\" isolated on", isolation, StringComparison.Ordinal);
        Assert.Single(request.Interfaces);
    }

    [Fact]
    public async Task AgentAuthentication_RejectsGuestRouteOnPlatformListener()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gzctf-auth-{Guid.NewGuid():N}");
        try
        {
            var options = Options.Create(new AgentConfig
            {
                AuthToken = "platform-token",
                ListenPort = 5001,
                GuestManagement = new GuestManagementConfig
                {
                    Enabled = true,
                    ListenPort = 5443,
                    StateRoot = root
                }
            });
            var reached = false;
            var middleware = new AgentEndpointAuthenticationMiddleware(
                _ =>
                {
                    reached = true;
                    return Task.CompletedTask;
                },
                options,
                new GuestCertificateAuthority(options));
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/guest/v1/enroll";
            context.Request.Method = HttpMethods.Post;
            context.Connection.LocalPort = 5001;
            context.Response.Body = new MemoryStream();
            context.Response.Headers[AgentProtocolHeaders.CorrelationId] = "test-correlation";

            await middleware.InvokeAsync(context);

            Assert.False(reached);
            Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static GuestAssetIdentity Identity()
    {
        const string vmName = "tl42-ad-dc";
        const int generation = 3;
        return new GuestAssetIdentity(
            Guid.CreateVersion7(),
            42,
            generation,
            "ad-dc",
            vmName,
            VmDomainBuilder.BuildStableDomainId(vmName, generation),
            0);
    }
}
