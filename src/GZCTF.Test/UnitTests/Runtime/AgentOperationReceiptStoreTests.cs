using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using GZCTF.Agent.Services.Vm;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GZCTF.Test.UnitTests.Runtime;

public sealed class AgentOperationReceiptStoreTests
{
    [Fact]
    public async Task CompletedReceipt_ReplaysAcrossStoreRestartAndRejectsIdentityConflict()
    {
        var root = Path.Combine(Path.GetTempPath(), "gzctf-agent-receipt-" + Guid.NewGuid().ToString("N"));
        var config = Options.Create(new AgentConfig
        {
            OperationStateRoot = root
        });
        try
        {
            var operationId = Guid.NewGuid();
            var calls = 0;
            var first = new AgentOperationReceiptStore(new AgentResourceLock(), config);
            var response = await first.ExecuteAsync(
                "vm-build", operationId, new ReceiptRequest("source-a",
                    new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" }), _ =>
                {
                    calls++;
                    return Task.FromResult(new ReceiptResponse("sha256:artifact-a"));
                }, CancellationToken.None);

            var restarted = new AgentOperationReceiptStore(new AgentResourceLock(), config);
            var replay = await restarted.ExecuteAsync(
                "vm-build", operationId, new ReceiptRequest("source-a",
                    new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" }), _ =>
                {
                    calls++;
                    return Task.FromResult(new ReceiptResponse("unexpected"));
                }, CancellationToken.None);

            Assert.Equal("sha256:artifact-a", response.Digest);
            Assert.Equal(response, replay);
            Assert.Equal(1, calls);
            var conflict = await Assert.ThrowsAsync<AgentOperationException>(() => restarted.ExecuteAsync(
                "vm-build", operationId, new ReceiptRequest("source-b", new Dictionary<string, string>()), _ =>
                    Task.FromResult(new ReceiptResponse("unexpected")), CancellationToken.None));
            Assert.Equal("operation_identity_conflict", conflict.Code);
            Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private sealed record ReceiptRequest(string Source, IReadOnlyDictionary<string, string> Files);
    private sealed record ReceiptResponse(string Digest);
}
