using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Account;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public sealed class ImageTemplateRemoteAccessTests(GZCTFApplicationFactory factory)
{
    private const string Password = "ImageCredential!Pass123";

    [Fact]
    public async Task OwnerTeacher_CanConfigureWindowsRdpAccess()
    {
        var teacher = await TestDataSeeder.CreateUserAsync(
            factory.Services, TestDataSeeder.RandomName(), Password, role: Role.Teacher);
        var imageId = await CreateTemplateAsync(teacher.Id, OSType.Windows, ImageType.Qcow2);
        using var client = await CreateAuthenticatedClientAsync(teacher.UserName!);

        using var response = await client.PatchAsJsonAsync(
            $"/api/v1/image-templates/{imageId}/remote-access",
            new UpdateImageRemoteAccessModel(
                true, TeamLabRemoteProtocol.Rdp, 3389, "player", "fixed-test-password"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("enabled").GetBoolean());
        Assert.Equal("rdp", body.GetProperty("protocol").GetString());
        Assert.Equal(3389, body.GetProperty("port").GetInt32());
        Assert.Equal("player", body.GetProperty("username").GetString());
        Assert.True(body.GetProperty("hasCredential").GetBoolean());
        Assert.Equal(JsonValueKind.Number, body.GetProperty("updatedAt").ValueKind);
        Assert.True(await HasRemoteAccessAsync(imageId));
    }

    [Fact]
    public async Task NonOwnerTeacher_CannotConfigureRemoteAccess()
    {
        var owner = await TestDataSeeder.CreateUserAsync(
            factory.Services, TestDataSeeder.RandomName(), Password, role: Role.Teacher);
        var otherTeacher = await TestDataSeeder.CreateUserAsync(
            factory.Services, TestDataSeeder.RandomName(), Password, role: Role.Teacher);
        var imageId = await CreateTemplateAsync(owner.Id, OSType.Windows, ImageType.Qcow2);
        using var client = await CreateAuthenticatedClientAsync(otherTeacher.UserName!);

        using var response = await client.PatchAsJsonAsync(
            $"/api/v1/image-templates/{imageId}/remote-access",
            new UpdateImageRemoteAccessModel(
                true, TeamLabRemoteProtocol.Rdp, 3389, "player", "fixed-test-password"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(await HasRemoteAccessAsync(imageId));
    }

    [Theory]
    [InlineData(OSType.Linux, ImageType.Qcow2, TeamLabRemoteProtocol.Rdp)]
    [InlineData(OSType.Windows, ImageType.Docker, TeamLabRemoteProtocol.Rdp)]
    [InlineData(OSType.Windows, ImageType.Qcow2, TeamLabRemoteProtocol.Ssh)]
    public async Task UnsupportedRemoteAccessConfiguration_IsRejected(
        OSType osType,
        ImageType imageType,
        TeamLabRemoteProtocol protocol)
    {
        var admin = await TestDataSeeder.CreateUserAsync(
            factory.Services, TestDataSeeder.RandomName(), Password, role: Role.Admin);
        var imageId = await CreateTemplateAsync(null, osType, imageType);
        using var client = await CreateAuthenticatedClientAsync(admin.UserName!);

        using var response = await client.PatchAsJsonAsync(
            $"/api/v1/image-templates/{imageId}/remote-access",
            new UpdateImageRemoteAccessModel(
                true, protocol, protocol == TeamLabRemoteProtocol.Rdp ? 3389 : 22,
                "player", "fixed-test-password"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await HasRemoteAccessAsync(imageId));
    }

    [Fact]
    public async Task EnabledVmRemoteAccess_RequiresUsername()
    {
        var admin = await TestDataSeeder.CreateUserAsync(
            factory.Services, TestDataSeeder.RandomName(), Password, role: Role.Admin);
        var imageId = await CreateTemplateAsync(null, OSType.Windows, ImageType.Qcow2);
        using var client = await CreateAuthenticatedClientAsync(admin.UserName!);

        using var response = await client.PatchAsJsonAsync(
            $"/api/v1/image-templates/{imageId}/remote-access",
            new UpdateImageRemoteAccessModel(
                true, TeamLabRemoteProtocol.Rdp, 3389, null, "fixed-test-password"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await HasRemoteAccessAsync(imageId));
    }

    [Fact]
    public async Task SharedTemplate_IsReportedReadOnlyForTeacher()
    {
        var teacher = await TestDataSeeder.CreateUserAsync(
            factory.Services, TestDataSeeder.RandomName(), Password, role: Role.Teacher);
        var imageId = await CreateTemplateAsync(null, OSType.Windows, ImageType.Qcow2);
        using var client = await CreateAuthenticatedClientAsync(teacher.UserName!);

        using var response = await client.GetAsync($"/api/v1/image-templates/{imageId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("canManage").GetBoolean());
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string userName)
    {
        var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync(
            "/api/Account/LogIn",
            new LoginModel { UserName = userName, Password = Password });
        login.EnsureSuccessStatusCode();
        return client;
    }

    private async Task<int> CreateTemplateAsync(
        Guid? ownerId,
        OSType osType,
        ImageType imageType,
        ImageStatus status = ImageStatus.Ready)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var template = new ImageTemplate
        {
            Name = $"credential-image-{Guid.NewGuid():N}",
            OSType = osType,
            ImageType = imageType,
            Status = status,
            CreatedById = ownerId
        };
        context.ImageTemplates.Add(template);
        await context.SaveChangesAsync();
        return template.Id;
    }

    private async Task<bool> HasRemoteAccessAsync(int imageId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.ImageTemplateRemoteAccesses.AnyAsync(item => item.ImageTemplateId == imageId);
    }
}
