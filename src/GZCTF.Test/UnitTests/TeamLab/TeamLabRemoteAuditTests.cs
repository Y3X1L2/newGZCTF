using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Infrastructure.Telemetry;
using GZCTF.Models;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Storage.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabRemoteAuditTests
{
    [Fact]
    public async Task InterruptedObjectWriteLeavesRecoverableReservation()
    {
        await using var db = Context();
        var session = await Seed(db);
        var objects = new Dictionary<string, byte[]>();
        await Assert.ThrowsAsync<IOException>(() => Service(db, objects, failWrites: true)
            .GenerateAsync(session.PublicId, session.RequestedByUserId, false, default));
        var reserved = await db.Set<TeamLabRemoteAuditFile>().SingleAsync();
        Assert.Null(reserved.ReadyAt);
        Assert.True(reserved.Size > 0);
        var recovered = Service(db, objects);
        await recovered.MaintainAsync(default);
        Assert.NotNull((await db.Set<TeamLabRemoteAuditFile>().SingleAsync()).ReadyAt);
        Assert.Single(objects);
    }

    [Fact]
    public async Task ArchiveDownloadAndExpiryUseRealObjectsAndMetadata()
    {
        await using var db = Context();
        var session = await Seed(db);
        var objects = new Dictionary<string, byte[]>();
        var service = Service(db, objects);
        await service.GenerateAsync(session.PublicId, session.RequestedByUserId, false, default);
        await service.GenerateAsync(session.PublicId, session.RequestedByUserId, false, default);
        var file = Assert.Single((await service.ListAsync(session.PublicId, session.RequestedByUserId, false, default)).Items);
        Assert.Single(objects);
        var download = await service.DownloadAsync(session.PublicId, file.Id, session.RequestedByUserId, false, default);
        using var json = System.Text.Json.JsonDocument.Parse(download.Content);
        Assert.False(json.RootElement.GetProperty("contentRecorded").GetBoolean());
        Assert.Equal(session.PublicId, json.RootElement.GetProperty("sessionId").GetGuid());
        Assert.Equal(file.Size, download.Content.Length);
        session.EndedAt = DateTimeOffset.UtcNow.AddDays(-91);
        (await db.Set<TeamLabRemoteAuditFile>().SingleAsync()).ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();
        await service.MaintainAsync(default);
        Assert.Empty(objects);
        Assert.Empty(await db.Set<TeamLabRemoteAuditFile>().ToArrayAsync());
        Assert.Equal("expired", (await service.ListAsync(session.PublicId, session.RequestedByUserId, false, default)).State);
    }

    [Fact]
    public async Task ForeignOperatorAndChangedContentAreRejected()
    {
        await using var db = Context();
        var session = await Seed(db);
        var objects = new Dictionary<string, byte[]>();
        var service = Service(db, objects);
        var denied = await Assert.ThrowsAsync<TeamLabApiContractException>(() => service.GenerateAsync(session.PublicId, Guid.NewGuid(), false, default));
        Assert.Equal(403, denied.StatusCode);
        await service.GenerateAsync(session.PublicId, session.RequestedByUserId, false, default);
        var file = await db.Set<TeamLabRemoteAuditFile>().SingleAsync();
        objects[file.RelativePath][0] ^= 1;
        var corrupted = await Assert.ThrowsAsync<TeamLabApiContractException>(() => service.DownloadAsync(session.PublicId, file.Id, session.RequestedByUserId, false, default));
        Assert.Equal("remote_audit_integrity_failed", corrupted.Code);
    }

    [Fact]
    public async Task QuotaAndActiveSessionNeverProduceSuccessMetadata()
    {
        await using var db = Context();
        var session = await Seed(db);
        var objects = new Dictionary<string, byte[]>();
        var service = Service(db, objects, 1);
        var quota = await Assert.ThrowsAsync<TeamLabApiContractException>(() => service.GenerateAsync(session.PublicId, session.RequestedByUserId, false, default));
        Assert.Equal("remote_audit_quota_exceeded", quota.Code);
        session.EndedAt = null;
        session.Status = TeamLabRemoteSessionStatus.Connected;
        await db.SaveChangesAsync();
        var active = await Assert.ThrowsAsync<TeamLabApiContractException>(() => service.GenerateAsync(session.PublicId, session.RequestedByUserId, false, default));
        Assert.Equal("remote_audit_session_active", active.Code);
        Assert.Empty(objects);
        Assert.Empty(await db.Set<TeamLabRemoteAuditFile>().ToArrayAsync());
    }

    private static TeamLabRemoteAuditService Service(AppDbContext db, Dictionary<string, byte[]> objects, long quota = 1024 * 1024, bool failWrites = false)
    {
        var storage = new Mock<IBlobStorage>();
        storage.Setup(item => item.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), false, It.IsAny<CancellationToken>()))
            .Returns(async (string path, Stream stream, bool _, CancellationToken token) => { if (failWrites) throw new IOException("fixture storage unavailable"); using var content = new MemoryStream(); await stream.CopyToAsync(content, token); objects[path] = content.ToArray(); });
        storage.Setup(item => item.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, CancellationToken _) => new MemoryStream(objects[path]));
        storage.Setup(item => item.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, CancellationToken _) => objects.ContainsKey(path));
        storage.Setup(item => item.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string path, CancellationToken _) => { objects.Remove(path); return Task.CompletedTask; });
        var leases = new Mock<IDistributedLeaseProvider>();
        leases.Setup(item => item.AcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IDistributedLease>(Mock.Of<IDistributedLease>()));
        return new(db, storage.Object, new TeamLabAuthorizationService(db, [], []), leases.Object,
            Options.Create(new TeamLabRemoteAuditOptions { MaxStorageBytes = quota }),
            new TeamLabEventRecorder(db, Mock.Of<IOperationalEventWriter>(), new OperationalCorrelation()),
            NullLogger<TeamLabRemoteAuditService>.Instance);
    }
    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static async Task<TeamLabRemoteSession> Seed(AppDbContext db)
    {
        var actor = Guid.NewGuid();
        var session = new TeamLabRemoteSession { Runtime = new TeamLabRuntime { CreatedById = actor }, RequestedByUserId = actor,
            Status = TeamLabRemoteSessionStatus.Ended, EndedAt = DateTimeOffset.UtcNow.AddMinutes(-1), Reason = "support request" };
        db.TeamLabRemoteSessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }
}
