using System;
using System.Linq;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Services.Fleet;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabRuntimeFoundationTests
{
    [Fact]
    public void QueueIdentity_DependsOnlyOnRuntime()
    {
        var request = DeploymentQueueRequest.TeamLab(
            42, 2, 1, runtimePublicId: Guid.Parse("01900000-0000-7000-8000-000000000042"));
        var ticket = DeploymentQueueTicket.Create(request);

        Assert.Equal("teamlab-runtime:42", ticket.ActiveIdentity);
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
                [new TeamLabRuntimeOverlayModel("entry", null, new System.Collections.Generic.Dictionary<string, string> { ["FLAG"] = "flag{secret}" })]),
            null,
            null);

        var protectedPayload = service.Protect(payload);
        var restored = service.Unprotect(protectedPayload);

        Assert.DoesNotContain("flag{secret}", protectedPayload, StringComparison.Ordinal);
        Assert.Equal("flag{secret}", restored.Create!.Overlays!.Single().Secrets!["FLAG"]);
    }

    private static Type[] Unwrap(Type type)
    {
        if (type.IsGenericType) return type.GetGenericArguments().SelectMany(Unwrap).ToArray();
        if (type.IsArray) return Unwrap(type.GetElementType()!);
        return [type];
    }
}
