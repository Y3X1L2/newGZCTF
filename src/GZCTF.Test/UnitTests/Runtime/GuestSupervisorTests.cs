using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.GuestControl.Contracts;
using GZCTF.GuestSupervisor;
using GZCTF.GuestSupervisor.Enrollment;
using GZCTF.GuestSupervisor.Lifecycle;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GZCTF.Test.UnitTests.Runtime;

public sealed class GuestSupervisorTests
{
    [Fact]
    public async Task Bootstrap_CompletedStepIsNotExecutedAgain()
    {
        await using var fixture = await SupervisorFixture.CreateAsync(reboot: false);

        var first = await fixture.Executor.ExecuteAsync(fixture.Checkpoint(0), CancellationToken.None);
        var second = await fixture.Executor.ExecuteAsync(fixture.Checkpoint(0), CancellationToken.None);

        Assert.True(first.Completed);
        Assert.True(second.Completed);
        Assert.Equal("x" + Environment.NewLine, await File.ReadAllTextAsync(fixture.MarkerPath));
    }

    [Fact]
    public async Task Bootstrap_InterruptedStepFailsClosedWithoutRerun()
    {
        await using var fixture = await SupervisorFixture.CreateAsync(reboot: false);
        await fixture.ExecutionStore.SaveAsync(new GuestBootstrapExecutionState(
            fixture.IntentDigest,
            fixture.ArtifactDigest,
            0,
            new Dictionary<string, GuestStepState>
            {
                ["install"] = new("Running", null, null, 0, DateTimeOffset.UtcNow)
            }, []), CancellationToken.None);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Executor.ExecuteAsync(fixture.Checkpoint(0), CancellationToken.None));

        Assert.StartsWith("guest_bootstrap_step_interrupted", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.MarkerPath));
    }

    [Fact]
    public async Task Bootstrap_RebootResumesOnlyAtNextBootEpoch()
    {
        await using var fixture = await SupervisorFixture.CreateAsync(reboot: true);

        var requested = await fixture.Executor.ExecuteAsync(fixture.Checkpoint(0), CancellationToken.None);
        var sameBootError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Executor.ExecuteAsync(fixture.Checkpoint(0), CancellationToken.None));
        var resumed = await fixture.Executor.ExecuteAsync(fixture.Checkpoint(1), CancellationToken.None);

        Assert.True(requested.RequiresReboot);
        Assert.Equal("guest_bootstrap_reboot_not_observed", sameBootError.Message);
        Assert.True(resumed.Completed);
        Assert.Equal(1, resumed.RebootCount);
        Assert.Equal("x" + Environment.NewLine, await File.ReadAllTextAsync(fixture.MarkerPath));
    }

    [Fact]
    public async Task Bootstrap_TamperedManifestIsRejectedBeforeArtifactUse()
    {
        await using var fixture = await SupervisorFixture.CreateAsync(reboot: false, tamperManifest: true);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Executor.ExecuteAsync(fixture.Checkpoint(0), CancellationToken.None));

        Assert.Equal("guest_bootstrap_signature_invalid", error.Message);
        Assert.Equal(0, fixture.Gateway.DownloadCount);
    }

    [Fact]
    public async Task Bootstrap_StandardTarDotPrefixIsAccepted()
    {
        await using var fixture = await SupervisorFixture.CreateAsync(reboot: false, dotPrefixedArchive: true);

        var result = await fixture.Executor.ExecuteAsync(fixture.Checkpoint(0), CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal("x" + Environment.NewLine, await File.ReadAllTextAsync(fixture.MarkerPath));
    }

    [Fact]
    public async Task Checkpoint_BootIdentityChangeIncrementsEpochOnce()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gzctf-checkpoint-{Guid.NewGuid():N}");
        try
        {
            var identity = Identity();
            var store = new GuestCheckpointStore(root);
            var initial = new GuestLocalCheckpoint(
                identity, GuestLifecycleStage.RebootRequested, 4, "sha256:intent", "sha256:event", true,
                "an-old-boot", DateTimeOffset.UtcNow);
            await store.SaveAsync(initial, CancellationToken.None);

            var changed = await store.LoadAsync(identity, initial.IntentDigest, CancellationToken.None);
            await store.SaveAsync(changed, CancellationToken.None);
            var stable = await store.LoadAsync(identity, initial.IntentDigest, CancellationToken.None);

            Assert.True(changed.BootChanged);
            Assert.Equal(1, changed.Identity.BootEpoch);
            Assert.False(stable.BootChanged);
            Assert.Equal(1, stable.Identity.BootEpoch);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Contract_AllowsExactlyOneEpochAfterPersistedRebootRequest()
    {
        var identity = Identity();
        var valid = Event(identity with { BootEpoch = 1 }, GuestLifecycleStage.GuestReenrolledAfterBoot);

        GuestControlContractValidator.ValidateBootTransition(
            identity, GuestLifecycleStage.RebootRequested, valid);
        Assert.Throws<GuestControlProtocolException>(() =>
            GuestControlContractValidator.ValidateBootTransition(
                identity, GuestLifecycleStage.BootstrapRunning, valid));
        Assert.Throws<GuestControlProtocolException>(() =>
            GuestControlContractValidator.ValidateBootTransition(
                identity, GuestLifecycleStage.RebootRequested,
                valid with { Identity = identity with { BootEpoch = 2 } }));
    }

    private static GuestAssetIdentity Identity() => new(
        Guid.Parse("019f7000-0000-7000-8000-000000000101"),
        42, 3, "service", "tl42-service",
        Guid.Parse("019f7000-0000-7000-8000-000000000102"), 0);

    private static GuestLifecycleEvent Event(GuestAssetIdentity identity, GuestLifecycleStage stage) => new(
        GuestControlProtocol.SchemaVersion, identity, 5, stage, GuestLifecycleOutcome.Ready,
        DateTimeOffset.UtcNow, "sha256:event");

    private sealed class SupervisorFixture : IAsyncDisposable
    {
        private SupervisorFixture(
            string root,
            string markerPath,
            string intentDigest,
            string artifactDigest,
            GuestBootstrapExecutionStore executionStore,
            GuestBootstrapPackageExecutor executor,
            FakeGateway gateway)
        {
            Root = root;
            MarkerPath = markerPath;
            IntentDigest = intentDigest;
            ArtifactDigest = artifactDigest;
            ExecutionStore = executionStore;
            Executor = executor;
            Gateway = gateway;
        }

        public string Root { get; }
        public string MarkerPath { get; }
        public string IntentDigest { get; }
        public string ArtifactDigest { get; }
        public GuestBootstrapExecutionStore ExecutionStore { get; }
        public GuestBootstrapPackageExecutor Executor { get; }
        public FakeGateway Gateway { get; }

        public GuestLocalCheckpoint Checkpoint(long bootEpoch) => new(
            Identity() with { BootEpoch = bootEpoch }, GuestLifecycleStage.BootstrapRunning, 4,
            IntentDigest, "sha256:bootstrap-running", true, $"boot-{bootEpoch}", DateTimeOffset.UtcNow);

        public static async Task<SupervisorFixture> CreateAsync(
            bool reboot,
            bool tamperManifest = false,
            bool dotPrefixedArchive = false)
        {
            var root = Path.Combine(Path.GetTempPath(), $"gzctf-supervisor-{Guid.NewGuid():N}");
            var source = Path.Combine(root, "source");
            var bin = Path.Combine(source, "bin");
            Directory.CreateDirectory(bin);
            var marker = Path.Combine(root, "marker.txt");
            var entrypoint = OperatingSystem.IsWindows() ? "bin/install.ps1" : "bin/install.sh";
            var script = OperatingSystem.IsWindows()
                ? $"Add-Content -LiteralPath '{marker.Replace("'", "''", StringComparison.Ordinal)}' -Value 'x'; exit {(reboot ? 3010 : 0)}"
                : $"#!/bin/sh\nprintf 'x\\n' >> '{marker.Replace("'", "'\\''", StringComparison.Ordinal)}'\nexit {(reboot ? 194 : 0)}\n";
            await File.WriteAllTextAsync(Path.Combine(source, entrypoint.Replace('/', Path.DirectorySeparatorChar)), script);
            var tar = Path.Combine(root, "package.tar");
            var archive = Path.Combine(root, "package.tar.gz");
            if (dotPrefixedArchive)
            {
                await using var tarOutput = File.Create(tar);
                using var writer = new TarWriter(tarOutput, leaveOpen: false);
                writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "./"));
                writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "./bin"));
                await using var scriptInput = File.OpenRead(Path.Combine(source,
                    entrypoint.Replace('/', Path.DirectorySeparatorChar)));
                var scriptEntry = new PaxTarEntry(TarEntryType.RegularFile, $"./{entrypoint}")
                {
                    DataStream = scriptInput,
                    Mode = (UnixFileMode)0x1ED
                };
                writer.WriteEntry(scriptEntry);
            }
            else
            {
                TarFile.CreateFromDirectory(source, tar, includeBaseDirectory: false);
            }
            await using (var input = File.OpenRead(tar))
            await using (var output = File.Create(archive))
            await using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize))
                await input.CopyToAsync(gzip);
            var artifactDigest = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(archive)));
            var os = OperatingSystem.IsWindows() ? "Windows" : "Linux";
            var manifest = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                operatingSystems = new[] { os },
                assetKinds = new[] { "Vm" },
                requiredTemplateCapabilities = Array.Empty<string>(),
                parameters = Array.Empty<object>(),
                files = Array.Empty<object>(),
                steps = new[]
                {
                    new
                    {
                        id = "install",
                        entrypoint,
                        timeoutSeconds = 30,
                        runAs = "system",
                        reboot = reboot ? "IfRequested" : "None"
                    }
                },
                healthChecks = Array.Empty<object>(),
                maxReboots = reboot ? 1 : 0
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var signature = Convert.ToBase64String(signingKey.SignData(
                Encoding.UTF8.GetBytes(manifest), HashAlgorithmName.SHA256));
            if (tamperManifest) manifest += " ";
            var descriptor = new GuestServicePackageDescriptor(
                Guid.CreateVersion7(), 1, $"sha256:{artifactDigest}", new FileInfo(archive).Length,
                new Uri("https://127.0.0.1/artifacts"), manifest, signature,
                signingKey.ExportSubjectPublicKeyInfoPem());
            const string intentDigest = "sha256:test-intent";
            var intent = new GuestBootstrapIntent(
                GuestControlProtocol.SchemaVersion, GuestControlProtocol.SchemaVersion,
                Identity(), intentDigest, "sha256:prepared", descriptor.ArtifactDigest,
                DateTimeOffset.UtcNow.AddMinutes(5), descriptor, [], new Dictionary<string, string>());
            await new GuestIntentStore(root).SaveAsync(intent, CancellationToken.None);
            var config = new GuestSupervisorConfiguration(
                GuestControlProtocol.SchemaVersion, Identity(), new Uri("https://127.0.0.1/enroll"),
                "token", new string('a', 64), intentDigest, root);
            var gateway = new FakeGateway(archive);
            var executionStore = new GuestBootstrapExecutionStore(root);
            var executor = new GuestBootstrapPackageExecutor(
                config, gateway, executionStore, new GuestSecretStore(root),
                new GuestRemoteAccessProvisioner(),
                NullLogger<GuestBootstrapPackageExecutor>.Instance);
            return new SupervisorFixture(root, marker, intentDigest, artifactDigest, executionStore, executor, gateway);
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
            return ValueTask.CompletedTask;
        }
    }

    public sealed class FakeGateway(string artifactPath) : IGuestGatewayClient
    {
        public int DownloadCount { get; private set; }

        public Task<string> DownloadArtifactAsync(
            GuestServicePackageDescriptor descriptor,
            GuestAssetIdentity identity,
            CancellationToken cancellationToken)
        {
            DownloadCount++;
            return Task.FromResult(artifactPath);
        }

        public Task<GuestSecretResponse> FetchSecretsAsync(
            GuestAssetIdentity identity,
            IReadOnlyList<string> references,
            CancellationToken cancellationToken) => Task.FromResult(new GuestSecretResponse([]));
    }
}
