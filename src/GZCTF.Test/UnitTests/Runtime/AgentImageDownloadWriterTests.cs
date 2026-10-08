using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using Xunit;

namespace GZCTF.Test.UnitTests.Runtime;

public sealed class AgentImageDownloadWriterTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), $"gzctf-image-download-{Guid.NewGuid():N}");
    string Partial => Path.Combine(_root, "image.qcow2.part");
    public AgentImageDownloadWriterTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task LowCapacity_RejectsBeforeCreatingAFile()
    {
        var budget = AgentImageStorageBudgetTests.Create(_ => new AgentStorageSnapshot("device", 100));
        var writer = new AgentImageDownloadWriter(budget);
        using var response = Response(new byte[91]);
        var error = await Assert.ThrowsAsync<AgentOperationException>(() =>
            writer.CopyAsync(response, Partial, false, 91, CancellationToken.None));
        Assert.Equal("image.storage_capacity_insufficient", error.Code);
        Assert.False(File.Exists(Partial));
    }

    [Fact]
    public async Task Resume_ReservesOnlyRemainingBytesAndDoesNotBudgetAnotherCopyForRename()
    {
        await File.WriteAllBytesAsync(Partial, new byte[60]);
        long available = 50;
        var budget = AgentImageStorageBudgetTests.Create(_ => new AgentStorageSnapshot("device", available));
        var writer = new AgentImageDownloadWriter(budget);
        using var response = Response(new byte[40]);
        response.StatusCode = HttpStatusCode.PartialContent;
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(60, 99, 100);
        await writer.CopyAsync(response, Partial, true, 100, CancellationToken.None,
            (transferred, _) => available = 50 - (transferred - 60));
        File.Move(Partial, Path.Combine(_root, "image.qcow2"));
        Assert.Equal(100, new FileInfo(Path.Combine(_root, "image.qcow2")).Length);
        using var next = budget.Reserve(_root, 0, "cache-hit");
    }

    [Fact]
    public async Task ExternalDiskConsumption_StopsBeforeTheNextChunkAndReturnsItsOutstandingBudget()
    {
        long available = 200_010;
        var budget = AgentImageStorageBudgetTests.Create(_ => new AgentStorageSnapshot("device", available));
        var writer = new AgentImageDownloadWriter(budget);
        using var response = Response(new byte[150_000]);
        var error = await Assert.ThrowsAsync<AgentOperationException>(() =>
            writer.CopyAsync(response, Partial, false, 150_000, CancellationToken.None,
                (_, _) => available = 11));
        Assert.Equal("image.storage_capacity_insufficient", error.Code);
        Assert.Equal(81920, new FileInfo(Partial).Length);
        available = 100;
        using var next = budget.Reserve(_root, 90, "retry");
    }

    [Fact]
    public async Task OversizedPayload_CannotWriteBeyondTheAdmittedSize()
    {
        var budget = AgentImageStorageBudgetTests.Create(_ => new AgentStorageSnapshot("device", 100));
        var writer = new AgentImageDownloadWriter(budget);
        using var response = Response(new byte[41]);
        response.Content.Headers.ContentLength = 40;
        var error = await Assert.ThrowsAsync<AgentOperationException>(() =>
            writer.CopyAsync(response, Partial, false, 40, CancellationToken.None));
        Assert.Equal("image.size_mismatch", error.Code);
        Assert.Equal(0, new FileInfo(Partial).Length);
        using var next = budget.Reserve(_root, 90, "retry");
    }

    [Fact]
    public async Task ShortPayload_ReturnsBudgetAndRetainsPartialBytesForResume()
    {
        var budget = AgentImageStorageBudgetTests.Create(_ => new AgentStorageSnapshot("device", 100));
        var writer = new AgentImageDownloadWriter(budget);
        using var response = Response(new byte[30]);
        response.Content.Headers.ContentLength = 40;
        var error = await Assert.ThrowsAsync<AgentOperationException>(() =>
            writer.CopyAsync(response, Partial, false, 40, CancellationToken.None));
        Assert.Equal("image.size_mismatch", error.Code);
        Assert.Equal(30, new FileInfo(Partial).Length);
        using var next = budget.Reserve(_root, 90, "retry");
    }

    [Fact]
    public async Task ResponseWithoutSize_FailsClosedBeforeWriting()
    {
        var budget = AgentImageStorageBudgetTests.Create(_ => new AgentStorageSnapshot("device", 100));
        var writer = new AgentImageDownloadWriter(budget);
        using var response = Response(new byte[30]);
        response.Content.Headers.ContentLength = null;
        var error = await Assert.ThrowsAsync<AgentOperationException>(() =>
            writer.CopyAsync(response, Partial, false, null, CancellationToken.None));
        Assert.Equal("image.storage_size_unknown", error.Code);
        Assert.False(File.Exists(Partial));
    }

    [Fact]
    public async Task InvalidResumeRange_DoesNotAppendToTheExistingPartial()
    {
        await File.WriteAllBytesAsync(Partial, new byte[30]);
        var budget = AgentImageStorageBudgetTests.Create(_ => new AgentStorageSnapshot("device", 100));
        var writer = new AgentImageDownloadWriter(budget);
        using var response = Response(new byte[40]);
        response.StatusCode = HttpStatusCode.PartialContent;
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(20, 59, 60);
        var error = await Assert.ThrowsAsync<AgentOperationException>(() =>
            writer.CopyAsync(response, Partial, true, 60, CancellationToken.None));
        Assert.Equal("image.size_mismatch", error.Code);
        Assert.Equal(30, new FileInfo(Partial).Length);
    }

    [Fact]
    public async Task CancelledWriter_ReleasesCapacityWithoutDeletingItsResumeFile()
    {
        var budget = AgentImageStorageBudgetTests.Create(_ => new AgentStorageSnapshot("device", 100));
        var writer = new AgentImageDownloadWriter(budget);
        using var cancel = new CancellationTokenSource();
        using var response = Response(new byte[40]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            writer.CopyAsync(response, Partial, false, 40, cancel.Token, (_, _) => cancel.Cancel()));
        using var next = budget.Reserve(_root, 90, "retry");
        Assert.True(File.Exists(Partial));
    }

    static HttpResponseMessage Response(byte[] bytes) => new(HttpStatusCode.OK)
    { Content = new StreamContent(new MemoryStream(bytes)) };
}
