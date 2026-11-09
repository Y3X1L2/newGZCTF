using System;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Hubs;
using Xunit;

namespace GZCTF.Test.UnitTests.Identity;

public sealed class UserHubAccountConnectionsTests
{
    [Fact]
    public async Task Revocation_AbortsOnlyTheDeletedUsersConnectionsAndIsIdempotent()
    {
        var connections = new UserHubAccountConnections();
        var deleted = Guid.NewGuid();
        var retained = Guid.NewGuid();
        var aborted = 0;
        connections.Register(deleted, "first", () => aborted++);
        connections.Register(deleted, "second", () => aborted++);
        connections.Register(retained, "other", () => throw new InvalidOperationException("Unrelated connection"));
        connections.Register(deleted, "disconnected", () => throw new InvalidOperationException("Already disconnected"));
        connections.Remove("disconnected");
        await connections.PrepareAsync(deleted, CancellationToken.None);
        Assert.Equal(0, aborted);
        await connections.CompleteAsync(deleted, CancellationToken.None);
        await connections.CompleteAsync(deleted, CancellationToken.None);
        Assert.Equal(2, aborted);
        Assert.Equal(retained, Assert.Single(connections.UserIds));
    }
}
