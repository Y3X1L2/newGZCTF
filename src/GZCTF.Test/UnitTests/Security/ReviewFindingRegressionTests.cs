using System;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Services.Concurrency;
using GZCTF.Services.Fleet;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
    public async Task RecoverStaleCreatingTicketsAsync_FailsStaleCreatingTicketAndReleasesCapacity()
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
            CurrentContainers = 2,
            ReservedContainers = 2
        };
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.TeamLab(3, dockerSlots: 2, vmSlots: 0));
        ticket.Status = DeploymentQueueTicketStatus.Creating;
        ticket.TargetNodeId = node.Id;
        ticket.StartedAt = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(30);
        context.WorkerNodes.Add(node);
        context.DeploymentQueueTickets.Add(ticket);
        await context.SaveChangesAsync();
        var service = CreateQueueService(context);

        var recovered = await service.RecoverStaleCreatingTicketsAsync(TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.Equal(1, recovered);
        Assert.Equal(DeploymentQueueTicketStatus.Failed, ticket.Status);
        Assert.Equal(2, node.CurrentContainers);
        Assert.Equal(0, node.ReservedContainers);
    }

    static DeploymentQueueService CreateQueueService(AppDbContext context)
    {
        var lockService = new LocalSemaphoreLock(NullLogger<LocalSemaphoreLock>.Instance);
        var capacity = new FleetCapacityReservationService(context, lockService,
            NullLogger<FleetCapacityReservationService>.Instance);
        return new DeploymentQueueService(context, capacity, NullLogger<DeploymentQueueService>.Instance);
    }

    static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }
}
