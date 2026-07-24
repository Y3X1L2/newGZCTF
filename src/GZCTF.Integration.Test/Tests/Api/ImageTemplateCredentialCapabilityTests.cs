using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Account;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public sealed class ImageTemplateCredentialCapabilityTests(GZCTFApplicationFactory factory)
{
    private const string Password = "ImageCredential!Pass123";

    [Fact]
    public async Task OwnerTeacher_CanCertifyWindowsVmImage()
    {
        var teacher = await TestDataSeeder.CreateUserAsync(
            factory.Services, TestDataSeeder.RandomName(), Password, role: Role.Teacher);
        var imageId = await CreateTemplateAsync(teacher.Id, OSType.Windows, ImageType.Qcow2);
        using var client = await CreateAuthenticatedClientAsync(teacher.UserName!);

        using var response = await client.PatchAsJsonAsync(
            $"/api/v1/image-templates/{imageId}/instance-credentials",
            new { supported = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("supportsInstanceCredentials").GetBoolean());
        Assert.True(await ReadCapabilityAsync(imageId));
    }

    [Fact]
    public async Task NonOwnerTeacher_CannotCertifyImage()
    {
        var owner = await TestDataSeeder.CreateUserAsync(
            factory.Services, TestDataSeeder.RandomName(), Password, role: Role.Teacher);
        var otherTeacher = await TestDataSeeder.CreateUserAsync(
            factory.Services, TestDataSeeder.RandomName(), Password, role: Role.Teacher);
        var imageId = await CreateTemplateAsync(owner.Id, OSType.Windows, ImageType.Qcow2);
        using var client = await CreateAuthenticatedClientAsync(otherTeacher.UserName!);

        using var response = await client.PatchAsJsonAsync(
            $"/api/v1/image-templates/{imageId}/instance-credentials",
            new { supported = true });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(await ReadCapabilityAsync(imageId));
    }

    [Theory]
    [InlineData(OSType.Linux, ImageType.Qcow2)]
    [InlineData(OSType.Windows, ImageType.Docker)]
    public async Task UnsupportedImageType_CannotBeCertified(OSType osType, ImageType imageType)
    {
        var admin = await TestDataSeeder.CreateUserAsync(
            factory.Services, TestDataSeeder.RandomName(), Password, role: Role.Admin);
        var imageId = await CreateTemplateAsync(null, osType, imageType);
        using var client = await CreateAuthenticatedClientAsync(admin.UserName!);

        using var response = await client.PatchAsJsonAsync(
            $"/api/v1/image-templates/{imageId}/instance-credentials",
            new { supported = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await ReadCapabilityAsync(imageId));
    }

    [Theory]
    [InlineData(ImageStatus.Importing)]
    [InlineData(ImageStatus.Error)]
    [InlineData(ImageStatus.Deleting)]
    public async Task NonReadyWindowsVmImage_CannotBeCertified(ImageStatus status)
    {
        var admin = await TestDataSeeder.CreateUserAsync(
            factory.Services, TestDataSeeder.RandomName(), Password, role: Role.Admin);
        var imageId = await CreateTemplateAsync(null, OSType.Windows, ImageType.Qcow2, status);
        using var client = await CreateAuthenticatedClientAsync(admin.UserName!);

        using var response = await client.PatchAsJsonAsync(
            $"/api/v1/image-templates/{imageId}/instance-credentials",
            new { supported = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await ReadCapabilityAsync(imageId));
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

    private async Task<bool> ReadCapabilityAsync(int imageId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.ImageTemplates
            .Where(item => item.Id == imageId)
            .Select(item => item.SupportsInstanceCredentials)
            .SingleAsync();
    }
}
