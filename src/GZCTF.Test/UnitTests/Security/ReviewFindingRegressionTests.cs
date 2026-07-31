using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Infrastructure;
using GZCTF.Services.Fleet;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Security;

public class ReviewFindingRegressionTests
{
    [Fact]
    public async Task CanManageUserRecordAsync_DeniesTeacherForStudentOutsideManagedGroups()
    {
        await using var context = CreateContext();
        var teacher = new UserInfo { Id = Guid.NewGuid(), UserName = "teacher", Role = Role.Teacher };
        var student = new UserInfo { Id = Guid.NewGuid(), UserName = "student", Role = Role.Student };
        var group = new StudentGroup { Id = 1, Name = "other group" };
        context.Users.AddRange(teacher, student);
        context.StudentGroups.Add(group);
        context.StudentGroupMembers.Add(new StudentGroupMember { GroupId = group.Id, StudentId = student.Id });
        await context.SaveChangesAsync();

        var allowed = await UserManagementGuard.CanManageUserRecordAsync(context, teacher, student, CancellationToken.None);

        Assert.False(allowed);
    }

    [Fact]
    public async Task CanManageUserRecordAsync_AllowsTeacherForStudentInManagedGroup()
    {
        await using var context = CreateContext();
        var teacher = new UserInfo { Id = Guid.NewGuid(), UserName = "teacher", Role = Role.Teacher };
        var student = new UserInfo { Id = Guid.NewGuid(), UserName = "student", Role = Role.Student };
        var group = new StudentGroup { Id = 1, Name = "managed group" };
        context.Users.AddRange(teacher, student);
        context.StudentGroups.Add(group);
        context.StudentGroupManagers.Add(new StudentGroupManager { GroupId = group.Id, ManagerId = teacher.Id });
        context.StudentGroupMembers.Add(new StudentGroupMember { GroupId = group.Id, StudentId = student.Id });
        await context.SaveChangesAsync();

        var allowed = await UserManagementGuard.CanManageUserRecordAsync(context, teacher, student, CancellationToken.None);

        Assert.True(allowed);
    }

    [Fact]
    public void CanCaptainLeave_ReturnsFalseForCurrentCaptain()
    {
        var captainId = Guid.NewGuid();
        var team = new Team { CaptainId = captainId };

        Assert.False(TeamPolicy.CanCaptainLeave(team, captainId));
    }

    [Fact]
    public void CanKickMember_ReturnsFalseWhenCaptainTargetsSelf()
    {
        var captainId = Guid.NewGuid();
        var team = new Team { CaptainId = captainId };

        Assert.False(TeamPolicy.CanKickMember(team, captainId));
    }

    [Fact]
    public void CanTransferTo_ReturnsFalseWhenNewCaptainIsNotMember()
    {
        var team = new Team();
        var outsider = new UserInfo { Id = Guid.NewGuid(), UserName = "outsider" };

        Assert.False(TeamPolicy.CanTransferTo(team, outsider));
    }

    [Fact]
    public void RedactAnswerForLog_DoesNotExposeRawFlag()
    {
        var redacted = SubmissionLogRedactor.RedactAnswer("flag{super-secret-value}");

        Assert.DoesNotContain("super-secret-value", redacted, StringComparison.Ordinal);
        Assert.StartsWith("sha256:", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecoverStaleCreatingTicketsAsync_ReplaysStableCreateAndKeepsCapacityReserved()
    {
        await using var context = CreateContext();
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Name = "node-a",
            HostAddress = "10.24.0.30",
            Status = NodeStatus.Online,
            IsSchedulable = true,
            IsLocal = true,
            Capabilities = NodeCapability.Docker,
            MaxContainers = 10,
            CurrentContainers = 2
        };
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(1, 2, 3));
        ticket.Status = DeploymentQueueTicketStatus.Running;
        ticket.TargetNodeId = node.Id;
        ticket.StartedAt = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(30);
        context.WorkerNodes.Add(node);
        context.DeploymentQueueTickets.Add(ticket);
        context.FleetCapacityReservations.Add(new FleetCapacityReservation
        {
            DeploymentQueueTicketId = ticket.Id,
            WorkerNodeId = node.Id,
            DockerSlots = 1,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        });
        await context.SaveChangesAsync();
        var service = CreateReconciliationService(context);

        var recovered = await service.ReconcileAsync(
            Guid.CreateVersion7(), TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.Equal(1, recovered.ReplayedCount);
        Assert.Equal(DeploymentQueueTicketStatus.Scheduled, ticket.Status);
        Assert.Equal(DeploymentStage.NodeExecutionWaiting, ticket.Stage);
        Assert.Equal(1, ticket.AttemptCount);
        Assert.Equal(2, node.CurrentContainers);
        Assert.Equal(CapacityReservationStatus.Active,
            (await context.FleetCapacityReservations.SingleAsync()).Status);
    }

    static DeploymentQueueService CreateQueueService(AppDbContext context)
    {
        var lockService = new LocalDevelopmentLeaseProvider();
        var capacity = new FleetCapacityReservationService(context, lockService,
            NullLogger<FleetCapacityReservationService>.Instance);
        return new DeploymentQueueService(context, capacity, NullLogger<DeploymentQueueService>.Instance);
    }

    static RuntimeFactReconciliationService CreateReconciliationService(AppDbContext context)
    {
        var lockService = new LocalDevelopmentLeaseProvider();
        var writer = new EfOperationalEventWriter(context, NullLogger<EfOperationalEventWriter>.Instance);
        var capacity = new FleetCapacityReservationService(context, lockService,
            new NodeCapacitySnapshotService(context),
            new NodeEligibilityEvaluator(Options.Create(new RuntimeSchedulingOptions())),
            writer,
            NullLogger<FleetCapacityReservationService>.Instance);
        var agent = new AgentClient(
            new Mock<IHttpClientFactory>().Object,
            new Mock<IServiceScopeFactory>().Object,
            new ConfigurationBuilder().Build(),
            NullLogger<AgentClient>.Instance);
        return new RuntimeFactReconciliationService(context, agent, capacity,
            new GZCTF.Modules.TeamLab.Application.TeamLabRuntimeRecoveryPolicy(
                Options.Create(new GZCTF.Models.Internal.TeamLabNetworkConfig())),
            new PollingDeploymentQueueWakeup(), writer,
            NullLogger<RuntimeFactReconciliationService>.Instance);
    }

    static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }
}
