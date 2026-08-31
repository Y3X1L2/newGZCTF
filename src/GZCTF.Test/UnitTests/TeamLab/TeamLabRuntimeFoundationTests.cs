using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Services.Fleet;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabRuntimeFoundationTests
{
    [Fact]
    public void QueueIdentity_SeparatesRuntimeOperationAndGeneration()
    {
        var request = DeploymentQueueRequest.TeamLab(
            42, 2, 1, runtimePublicId: Guid.Parse("01900000-0000-7000-8000-000000000042"));
        var ticket = DeploymentQueueTicket.Create(request);

        Assert.Equal("Create:teamlab-runtime:42:1", ticket.ActiveIdentity);
        Assert.Null(ticket.GameId);
        Assert.Null(ticket.OwnerTeamId);
        Assert.Equal("teamlab-runtime", ticket.SubjectType);
    }

    [Fact]
    public void RuntimeContract_DoesNotExposeGameTeamOrWorkerNode()
    {
        var names = typeof(TeamLabRuntimeProjectionModel).GetProperties().Select(item => item.Name).ToHashSet();

        Assert.DoesNotContain("GameId", names);
        Assert.DoesNotContain("TeamId", names);
        Assert.DoesNotContain("WorkerNodeId", names);
        Assert.Contains("Id", names);
        Assert.Contains("ReleaseId", names);
        Assert.Contains("Generation", names);
        Assert.Contains("CurrentOperationId", names);
        Assert.Contains("DeploymentQueueTicketId", names);
        Assert.Contains("ControlScopeId", names);
        Assert.Contains("ReleaseVersion", names);
    }

    [Fact]
    public void FailurePresentation_UsesStableContractWithoutRawDiagnostic()
    {
        var runtimeId = Guid.NewGuid();
        var ticket = new DeploymentQueueTicket
        {
            ErrorCode = "resume_blocked",
            ErrorMessage = "node=worker-secret command=/usr/bin/private",
            Stage = DeploymentStage.NodeExecutionWaiting,
            Retryable = false
        };

        var failure = TeamLabFailurePresentation.ForRuntime(
            TeamLabRuntimeStatus.Failed, ticket, runtimeId);

        Assert.NotNull(failure);
        Assert.Equal("resume_blocked", failure!.Code);
        Assert.Contains("wait_for_node", failure.Actions);
        Assert.Equal(runtimeId.ToString("D"), failure.ResourceId);
        Assert.DoesNotContain("worker-secret", failure.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("/usr/bin/private", failure.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void NodeExecutorPort_DoesNotExposeAgentDtos()
    {
        var exposed = typeof(ITeamLabNodeExecutor).GetMethods()
            .SelectMany(method => method.GetParameters().Select(item => item.ParameterType).Append(method.ReturnType))
            .SelectMany(Unwrap)
            .Where(type => type.Name.StartsWith("Agent", StringComparison.Ordinal) ||
                           type.Namespace?.Contains("Services.Fleet", StringComparison.Ordinal) == true)
            .Select(type => type.FullName)
            .ToArray();

        Assert.Empty(exposed);
    }

    [Fact]
    public void OperationPayload_IsEncryptedAtRest()
    {
        var service = new TeamLabRuntimeOperationPayloadProtector(new EphemeralDataProtectionProvider());
        var payload = new TeamLabRuntimeOperationPayload(
            new CreateTeamLabRuntimeModel(
                Guid.NewGuid(), "customer-1", null,
                [new TeamLabRuntimeOverlayModel("entry", new System.Collections.Generic.Dictionary<string, string> { ["FLAG"] = "flag{secret}" })]),
            null,
            null);

        var protectedPayload = service.Protect(payload);
        var restored = service.Unprotect(protectedPayload);

        Assert.DoesNotContain("flag{secret}", protectedPayload, StringComparison.Ordinal);
        Assert.Equal("flag{secret}", restored.Create!.Overlays!.Single().Secrets!["FLAG"]);
    }

    [Fact]
    public async Task RolloutTargetCommands_PreserveCallerIdempotencyIdentity()
    {
        var store = new CapturingSubmissionStore();
        var service = new TeamLabRuntimeOperationApplicationService(
            store,
            new TeamLabRuntimeOperationPayloadProtector(new EphemeralDataProtectionProvider()));
        var tokenId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var runtimeId = Guid.NewGuid();
        var rolloutId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var scopeId = Guid.NewGuid();

        await service.SubmitRolloutTargetLifecycleAsync(
            tokenId, actorId, "pause-command-1", runtimeId, rolloutId, targetId, scopeId, true,
            CancellationToken.None);
        await service.SubmitRolloutTargetRestartAsync(
            tokenId, actorId, "restart-command-1", runtimeId, rolloutId, targetId, scopeId,
            CancellationToken.None);

        Assert.Collection(store.Submissions,
            pause =>
            {
                Assert.Equal(tokenId, pause.ApiTokenId);
                Assert.Equal(actorId, pause.ActorUserId);
                Assert.Equal("pause-command-1", pause.IdempotencyKey);
                Assert.Equal(
                    $"POST:/api/open/v1/teamlab/rollouts/{rolloutId:D}/targets/{targetId:D}/pause#scope:{scopeId:D}",
                    pause.RouteKey);
            },
            restart =>
            {
                Assert.Equal(tokenId, restart.ApiTokenId);
                Assert.Equal("restart-command-1", restart.IdempotencyKey);
                Assert.Equal(
                    $"POST:/api/open/v1/teamlab/rollouts/{rolloutId:D}/targets/{targetId:D}/restart#scope:{scopeId:D}",
                    restart.RouteKey);
            });
    }

    private static Type[] Unwrap(Type type)
    {
        if (type.IsGenericType) return type.GetGenericArguments().SelectMany(Unwrap).ToArray();
        if (type.IsArray) return Unwrap(type.GetElementType()!);
        return [type];
    }

    private sealed class CapturingSubmissionStore : ITeamLabRuntimeOperationSubmissionStore
    {
        public List<TeamLabRuntimeOperationSubmission> Submissions { get; } = [];

        public Task<IdempotencyBeginResult> SubmitAsync(
            TeamLabRuntimeOperationSubmission submission,
            CancellationToken cancellationToken)
        {
            Submissions.Add(submission);
            return Task.FromResult(new IdempotencyBeginResult(new GZCTF.Modules.Audit.Domain.ApiOperation
            {
                Id = submission.Job.OperationId,
                ApiTokenId = submission.ApiTokenId,
                ActorUserId = submission.ActorUserId,
                RouteKey = submission.RouteKey,
                IdempotencyKey = submission.IdempotencyKey,
                RequestHash = submission.RequestHash
            }, false));
        }
    }
}
