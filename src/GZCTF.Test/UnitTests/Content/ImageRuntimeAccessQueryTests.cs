using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.Content.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Test.UnitTests.Content;

public sealed class ImageRuntimeAccessQueryTests
{
    [Fact]
    public async Task BatchReturnsOnlyImageIdentityAndConfigurationPresence()
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        context.ImageTemplates.Add(new ImageTemplate { Id = 42, Name = "vm", ImageHash = "digest-a",
            OSType = OSType.Windows, ImageType = ImageType.Qcow2 });
        var configuration = new ImageTemplateRemoteAccess
        {
            ImageTemplateId = 42, Enabled = true, Protocol = GZCTF.Modules.TeamLab.Domain.Runtime.TeamLabRemoteProtocol.Rdp,
            Port = 3389, Username = "operator", ProtectedSecret = Guid.NewGuid().ToString("N")
        };
        context.ImageTemplateRemoteAccesses.Add(configuration);
        await context.SaveChangesAsync();

        var result = await new EfImageRuntimeAccessQuery(context)
            .GetBatchAsync([42, 999], CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(OSType.Windows, result[42].OperatingSystem);
        Assert.Equal("rdp", result[42].RemoteProtocol);
        Assert.True(result[42].RemoteConfigured);
        Assert.DoesNotContain(result[42].GetType().GetProperties(), item =>
            item.Name is "Username" or "ProtectedSecret");

        configuration.Username = "  ";
        await context.SaveChangesAsync();
        var missingUsername = await new EfImageRuntimeAccessQuery(context)
            .GetBatchAsync([42], CancellationToken.None);
        Assert.False(missingUsername[42].RemoteConfigured);

        configuration.Username = "operator";
        configuration.ProtectedSecret = "  ";
        await context.SaveChangesAsync();
        var missingSecret = await new EfImageRuntimeAccessQuery(context)
            .GetBatchAsync([42], CancellationToken.None);
        Assert.False(missingSecret[42].RemoteConfigured);
    }
}
