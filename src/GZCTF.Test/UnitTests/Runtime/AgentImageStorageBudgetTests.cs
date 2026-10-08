using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using Xunit;

namespace GZCTF.Test.UnitTests.Runtime;

public sealed class AgentImageStorageBudgetTests
{
    [Fact]
    public async Task ConcurrentAdmission_CannotPromiseTheSameFreeBytesTwice()
    {
        var budget = Create(_ => new AgentStorageSnapshot("shared-device", 100));
        var leases = new ConcurrentBag<AgentImageStorageBudget.Lease>();
        var admitted = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
        {
            try { leases.Add(budget.Reserve("images", 30, "download")); return true; }
            catch (AgentOperationException error) when (error.Code == "image.storage_capacity_insufficient")
            { return false; }
        })));
        Assert.Equal(3, admitted.Count(value => value));
        foreach (var lease in leases) lease.Dispose();
        using var next = budget.Reserve("runtime-alias", 90, "download");
    }

    [Fact]
    public void SameFilesystem_DockerAndVmShareOneBudgetWhileDifferentFilesystemsRemainIndependent()
    {
        var budget = Create(path => new AgentStorageSnapshot(path == "separate" ? "device-2" : "device-1", 100));
        using var vm = budget.Reserve("images", 60, "vm");
        var error = Assert.Throws<AgentOperationException>(() => budget.Reserve("docker", 40, "docker"));
        Assert.Equal("Storage", error.Category);
        Assert.Equal("image.storage_capacity_insufficient", error.Code);
        Assert.Equal(507, error.StatusCode);
        Assert.True(error.Retryable);
        Assert.Contains("requiredAdditionalBytes=40", error.Message);
        Assert.Contains("committedBytes=60", error.Message);
        using var other = budget.Reserve("separate", 90, "download");
    }

    [Fact]
    public void WrittenBytes_AreRemovedFromFutureCommitmentsAndCountedByFilesystemFacts()
    {
        long available = 100;
        var budget = Create(_ => new AgentStorageSnapshot("device", available));
        using var first = budget.Reserve("images", 60, "download");
        first.CheckBeforeWrite(40);
        available -= 40;
        first.RecordWritten(40);
        using var second = budget.Reserve("docker-alias", 30, "download");
        Assert.Throws<AgentOperationException>(() => budget.Reserve("images", 1, "download"));
    }

    [Fact]
    public void ChangedFilesystem_FailsClosedInsteadOfUsingTheOldVolumeBudget()
    {
        var device = "first";
        var budget = Create(_ => new AgentStorageSnapshot(device, 100));
        using var writer = budget.Reserve("images", 60, "download");
        device = "replacement";
        var error = Assert.Throws<AgentOperationException>(() => writer.CheckBeforeWrite(1));
        Assert.Equal("image.storage_changed", error.Code);
    }

    [Fact]
    public void InvalidMeasurement_DoesNotFallBackToTheRootDisk()
    {
        var budget = Create(_ => throw new IOException("unavailable test filesystem"));
        var error = Assert.Throws<AgentOperationException>(() => budget.Reserve("images", 1, "download"));
        Assert.Equal("image.storage_unavailable", error.Code);
        Assert.DoesNotContain("unavailable test filesystem", error.Message);
    }

    [Fact]
    public void FinalGuard_ReportsConsumedSafetyMarginEvenAfterTheWriterSettlesAllFutureBytes()
    {
        long available = 100;
        var budget = Create(_ => new AgentStorageSnapshot("device", available));
        using var writer = budget.Reserve("docker", 80, "docker");
        writer.CompleteWrites();
        available = 9;
        var error = Assert.Throws<AgentOperationException>(() => writer.CheckCapacity());
        Assert.Equal("image.storage_capacity_insufficient", error.Code);
    }

    [Fact]
    public async Task CancelledWaiter_DoesNotReleaseTheSharedWritersCapacity()
    {
        var budget = Create(_ => new AgentStorageSnapshot("device", 100));
        var flight = new ImageTransferSingleFlight();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var caller = new CancellationTokenSource();
        var waiting = flight.RunAsync("template", async _ =>
        {
            using var writer = budget.Reserve("images", 90, "download");
            started.SetResult();
            try { await finish.Task; return true; }
            finally { writer.Dispose(); completed.SetResult(); }
        }, caller.Token);
        await started.Task;
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Throws<AgentOperationException>(() => budget.Reserve("docker-alias", 1, "download"));
        finish.SetResult();
        await completed.Task;
        using var next = budget.Reserve("images", 90, "download");
    }

    [Fact]
    public void DockerAllowance_IsPositiveConfigurableAndReleasedOnlyAfterTheWriterCompletes()
    {
        var budget = Create(_ => new AgentStorageSnapshot("device", 100));
        Assert.Equal(80, budget.DockerPullBudgetBytes);
        Assert.Equal(1800, budget.DockerPullTimeoutSeconds);
        using var docker = budget.Reserve("docker", budget.DockerPullBudgetBytes, "docker");
        Assert.Throws<AgentOperationException>(() => budget.Reserve("images", 11, "download"));
        docker.CompleteWrites();
        using var vm = budget.Reserve("images", 90, "download");
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentImageStorageBudget(
            new AgentImageStorageConfig { DockerPullBudgetBytes = 0 }, _ => new AgentStorageSnapshot("device", 100)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentImageStorageBudget(
            new AgentImageStorageConfig { DockerPullTimeoutSeconds = 0 }, _ => new AgentStorageSnapshot("device", 100)));
    }

    [Fact]
    public void Probe_UsesConfiguredDirectoryAndIdentifiesAliasesOnOneFilesystem()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gzctf-image-probe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var first = AgentStorageProbe.Read(directory);
            var nested = AgentStorageProbe.Read(Path.Combine(directory, "not-yet-created", "images"));
            Assert.Equal(first.FileSystemId, nested.FileSystemId);
            Assert.True(first.AvailableBytes > 0);
            Assert.Throws<IOException>(() => AgentStorageProbe.Read("relative-images"));
            if (OperatingSystem.IsLinux())
            {
                var link = directory + "-link";
                Directory.CreateSymbolicLink(link, directory);
                try { Assert.Equal(first.FileSystemId, AgentStorageProbe.Read(link).FileSystemId); }
                finally { Directory.Delete(link); }
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    internal static AgentImageStorageBudget Create(Func<string, AgentStorageSnapshot> probe) =>
        new(new AgentImageStorageConfig { SafetyMarginBytes = 10, DockerPullBudgetBytes = 80 }, probe);
}
