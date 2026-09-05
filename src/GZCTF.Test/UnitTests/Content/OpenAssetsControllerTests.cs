using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Modules.Content.Api;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Identity.Application;
using GZCTF.Repositories.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Content;

public sealed class OpenAssetsControllerTests
{
    [Fact]
    public void Endpoints_RequireAssetScopesAndDoNotExposeDeletion()
    {
        var controller = typeof(OpenAssetsController);
        Assert.Equal("scope:" + ApiTokenScopes.AssetsWrite,
            controller.GetMethod(nameof(OpenAssetsController.Upload))!.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal("scope:" + ApiTokenScopes.AssetsRead,
            controller.GetMethod(nameof(OpenAssetsController.Get))!.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.DoesNotContain(controller.GetMethods(), method => method.IsDefined(typeof(HttpDeleteAttribute)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Get_RequiresExplicitGrantForAnUnownedAsset(bool hasGrant)
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var file = new LocalFile { Hash = new string('a', 64), Name = "attachment.zip", FileSize = 4 };
        var blobs = new Mock<IBlobRepository>(MockBehavior.Strict);
        blobs.Setup(repository => repository.GetBlobByHash(file.Hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(file);
        var assets = new AssetApplicationService(context, blobs.Object,
            new IdempotencyService(new EfApiOperationStore(context)));
        var authorization = new Mock<IAuthorizationService>(MockBehavior.Strict);
        authorization.Setup(service => service.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(),
                It.Is<IEnumerable<IAuthorizationRequirement>>(requirements =>
                    requirements.OfType<ApiResourceRequirement>().Any(requirement =>
                        requirement.ResourceType == "asset" && requirement.ResourceId == file.Hash &&
                        requirement.RequireExplicitGrant))))
            .ReturnsAsync(hasGrant ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        var controller = new OpenAssetsController(assets, authorization.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([
                        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                        new Claim(ApiTokenClaimTypes.TokenId, Guid.NewGuid().ToString())
                    ], "test-token"))
                }
            }
        };

        var result = await controller.Get(file.Hash, default);

        if (hasGrant)
        {
            var asset = Assert.IsType<AssetDescriptor>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(file.Hash, asset.Hash);
        }
        else
        {
            Assert.IsType<NotFoundResult>(result);
            blobs.Verify(repository => repository.GetBlobByHash(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        authorization.VerifyAll();
    }
}
