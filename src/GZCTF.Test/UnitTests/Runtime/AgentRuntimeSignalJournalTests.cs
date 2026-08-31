using System;
using System.Net;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using GZCTF.Agent.Services.RuntimeSignals;
using GZCTF.Agent.Services.Vm;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Runtime;

public sealed class AgentRuntimeSignalJournalTests
{
    [Fact]
    public async Task Journal_PersistsMonotonicSignalsAndAcknowledgesReplay()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gzctf-signal-{Guid.NewGuid():N}");
        try
        {
            var journal = new AgentRuntimeSignalJournal(Options.Create(new AgentTeamLabConfig
            {
                RuntimeStateRoot = root
            }));
            var operationId = Guid.CreateVersion7();
            var draft = new AgentRuntimeSignalDraft(
                operationId, 42, 3, "docker", "container-1",
                AgentRuntimeSignalStage.ResourceCreated,
                AgentRuntimeSignalOutcome.Ready);

            var first = await journal.AppendAsync(draft, CancellationToken.None);
            var second = await journal.AppendAsync(
                draft with { Stage = AgentRuntimeSignalStage.NetworkReady }, CancellationToken.None);

            Assert.Equal(1, first.Sequence);
            Assert.Equal(2, second.Sequence);
            Assert.Equal([1L, 2L], (await journal.ReadPendingAsync(operationId, CancellationToken.None))
                .Select(item => item.Sequence));

            await journal.AcknowledgeAsync(operationId, 1, CancellationToken.None);
            var replay = await journal.ReadPendingAsync(operationId, CancellationToken.None);
            Assert.Single(replay);
            Assert.Equal(2, replay[0].Sequence);

            await journal.DeleteAsync(operationId);
            Assert.DoesNotContain(operationId, journal.ListOperations());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task VmReadiness_TracksDomainIdentityWithoutBlockingForGuestReady()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gzctf-vm-signal-{Guid.NewGuid():N}");
        try
        {
            var journal = new AgentRuntimeSignalJournal(Options.Create(new AgentTeamLabConfig
            {
                RuntimeStateRoot = root
            }));
            var publisher = new AgentRuntimeSignalPublisher(
                journal,
                Mock.Of<IHttpClientFactory>(),
                Options.Create(new AgentConfig()),
                NullLogger<AgentRuntimeSignalPublisher>.Instance);
            var coordinator = new VmRuntimeReadinessCoordinator(
                new KvmService(
                    Options.Create(new KvmConfig()),
                    new AgentResourceLock(),
                    NullLogger<KvmService>.Instance),
                new VmGuestAgentService(NullLogger<VmGuestAgentService>.Instance),
                journal,
                publisher,
                NullLogger<VmRuntimeReadinessCoordinator>.Instance);
            var operationId = Guid.CreateVersion7();

            await coordinator.TrackAsync(
                new CreateVmRequest
                {
                    OperationId = operationId,
                    RuntimeId = 91,
                    Generation = 4,
                    GuestReadyWarningAfterSeconds = 150,
                    GuestControl = new VmGuestControlConfig { OsType = VmInitOsType.Windows }
                },
                new CreateVmResponse
                {
                    VmName = "tl91-ad-dc",
                    NativeId = Guid.CreateVersion7().ToString("D"),
                    Generation = 4
                },
                CancellationToken.None);

            var history = await journal.ReadAllAsync(operationId, CancellationToken.None);
            var signal = Assert.Single(history);
            Assert.Equal(AgentRuntimeSignalStage.DomainRunning, signal.Stage);
            Assert.Equal(AgentRuntimeSignalOutcome.Ready, signal.Outcome);
            Assert.Equal("Windows", signal.Facts!["osType"]);
            Assert.Equal("150", signal.Facts["warningAfterSeconds"]);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Journal_CleansOnlyAcknowledgedRuntimeGeneration()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gzctf-signal-cleanup-{Guid.NewGuid():N}");
        try
        {
            var journal = new AgentRuntimeSignalJournal(Options.Create(new AgentTeamLabConfig
            {
                RuntimeStateRoot = root
            }));
            var operationId = Guid.CreateVersion7();
            var signal = await journal.AppendAsync(new AgentRuntimeSignalDraft(
                operationId,
                73,
                2,
                "container",
                "container-73",
                AgentRuntimeSignalStage.NetworkReady,
                AgentRuntimeSignalOutcome.Ready), CancellationToken.None);

            Assert.Equal(0, await journal.DeleteAcknowledgedGenerationAsync(73, 2, CancellationToken.None));
            await journal.AcknowledgeAsync(operationId, signal.Sequence, CancellationToken.None);
            Assert.Equal(1, await journal.DeleteAcknowledgedGenerationAsync(73, 2, CancellationToken.None));
            Assert.DoesNotContain(operationId, journal.ListOperations());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task PublishPendingAsync_AcknowledgesTerminalConflict()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gzctf-signal-conflict-{Guid.NewGuid():N}");
        try
        {
            var journal = new AgentRuntimeSignalJournal(Options.Create(new AgentTeamLabConfig
            {
                RuntimeStateRoot = root
            }));
            var operationId = Guid.CreateVersion7();
            var signal = await journal.AppendAsync(new AgentRuntimeSignalDraft(
                operationId,
                42,
                3,
                "docker",
                "container-1",
                AgentRuntimeSignalStage.ResourceCreated,
                AgentRuntimeSignalOutcome.Ready), CancellationToken.None);

            var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent("{\"message\":\"sequence reused\"}")
            });
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(item => item.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));
            var publisher = new AgentRuntimeSignalPublisher(
                journal,
                factory.Object,
                Options.Create(new AgentConfig
                {
                    ServerUrl = "http://127.0.0.1:8080",
                    NodeId = Guid.CreateVersion7()
                }),
                NullLogger<AgentRuntimeSignalPublisher>.Instance);

            await publisher.PublishPendingAsync(operationId, CancellationToken.None);

            Assert.Equal(1, signal.Sequence);
            Assert.Empty(await journal.ReadPendingAsync(operationId, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class StubHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }

}
