using System;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Test.UnitTests.Content;

public sealed class ImageRemoteAccessServiceTests
{
    [Fact]
    public async Task CompetitionWindowsProfile_RequiresReadyImageAndExistingRdpAccount()
    {
        await using var context = CreateContext();
        var template = new ImageTemplate
        {
            Id = 41,
            Name = "fixed-rdp",
            OSType = OSType.Windows,
            ImageType = ImageType.Qcow2,
            Status = ImageStatus.Ready
        };
        context.ImageTemplates.Add(template);
        await context.SaveChangesAsync();
        var service = new ImageRemoteAccessService(context, new EphemeralDataProtectionProvider());

        Assert.False(await service.HasCompetitionWindowsRdpProfileAsync(template.Id, CancellationToken.None));

        await service.UpdateAsync(template, new UpdateImageRemoteAccessModel(
            true,
            TeamLabRemoteProtocol.Rdp,
            3389,
            "player",
            RemoteCredentialMode.ExistingAccount,
            "fixed-password"), CancellationToken.None);

        var profile = await service.GetCompetitionWindowsRdpProfileAsync(template.Id, CancellationToken.None);
        Assert.NotNull(profile);
        Assert.Equal(3389, profile.Port);
        Assert.Equal("player", profile.Username);
        Assert.Equal("fixed-password", profile.Password);
    }

    [Fact]
    public async Task CompetitionWindowsProfile_RejectsPlatformGeneratedMode()
    {
        await using var context = CreateContext();
        var template = new ImageTemplate
        {
            Id = 42,
            Name = "managed-rdp",
            OSType = OSType.Windows,
            ImageType = ImageType.Qcow2,
            Status = ImageStatus.Ready
        };
        context.ImageTemplates.Add(template);
        await context.SaveChangesAsync();
        var service = new ImageRemoteAccessService(context, new EphemeralDataProtectionProvider());

        await service.UpdateAsync(template, new UpdateImageRemoteAccessModel(
            true,
            TeamLabRemoteProtocol.Rdp,
            3389,
            null,
            RemoteCredentialMode.PlatformGenerated,
            null), CancellationToken.None);

        Assert.False(await service.HasCompetitionWindowsRdpProfileAsync(template.Id, CancellationToken.None));
        Assert.Null(await service.GetCompetitionWindowsRdpProfileAsync(template.Id, CancellationToken.None));
    }

    [Fact]
    public async Task DisabledRemoteAccess_DoesNotRequireExistingAccountCredentials()
    {
        await using var context = CreateContext();
        var template = new ImageTemplate
        {
            Id = 43,
            Name = "disabled-rdp",
            OSType = OSType.Windows,
            ImageType = ImageType.Qcow2,
            Status = ImageStatus.Ready
        };
        context.ImageTemplates.Add(template);
        await context.SaveChangesAsync();
        var service = new ImageRemoteAccessService(context, new EphemeralDataProtectionProvider());

        var configuration = await service.UpdateAsync(template, new UpdateImageRemoteAccessModel(
            false,
            TeamLabRemoteProtocol.Rdp,
            3389,
            null,
            RemoteCredentialMode.ExistingAccount,
            null), CancellationToken.None);

        Assert.False(configuration.Enabled);
        Assert.False(configuration.HasCredential);
        Assert.False(await service.HasCompetitionWindowsRdpProfileAsync(template.Id, CancellationToken.None));
    }

    [Fact]
    public async Task CompetitionWindowsProfile_RejectsCredentialProtectedByAnotherKeyRing()
    {
        await using var context = CreateContext();
        var template = new ImageTemplate
        {
            Id = 44,
            Name = "stale-key-ring",
            OSType = OSType.Windows,
            ImageType = ImageType.Qcow2,
            Status = ImageStatus.Ready
        };
        context.ImageTemplates.Add(template);
        await context.SaveChangesAsync();
        var writer = new ImageRemoteAccessService(context, new EphemeralDataProtectionProvider());
        await writer.UpdateAsync(template, new UpdateImageRemoteAccessModel(
            true,
            TeamLabRemoteProtocol.Rdp,
            3389,
            "player",
            RemoteCredentialMode.ExistingAccount,
            "fixed-password"), CancellationToken.None);

        var reader = new ImageRemoteAccessService(context, new EphemeralDataProtectionProvider());

        Assert.False(await reader.HasCompetitionWindowsRdpProfileAsync(template.Id, CancellationToken.None));
        Assert.Null(await reader.GetCompetitionWindowsRdpProfileAsync(template.Id, CancellationToken.None));
    }

    private static AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
