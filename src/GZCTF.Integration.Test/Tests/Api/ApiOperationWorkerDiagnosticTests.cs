using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Integration.Test.Base;
using GZCTF.Integration.Test.Tests.Api.Fixtures;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using ApiTokenEntity = GZCTF.Modules.Identity.Domain.ApiToken;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public sealed class ApiOperationWorkerDiagnosticTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task Worker_ClaimsAndProcesses_WhenInvokedDirectly()
    {
        // Step 0: resolve all IApiOperationHandler instances (what ResolveHandlerKinds does)
        Exception? resolveError = null;
        string[]? kinds = null;
        try
        {
            using var resolveScope = factory.Services.CreateScope();
            kinds =
            [
                resolveScope.ServiceProvider
                    .GetRequiredKeyedService<IApiOperationHandler>(CompletingApiOperationHandler.OperationKind)
                    .Kind
            ];
        }
        catch (Exception exception)
        {
            resolveError = exception;
        }
        Assert.Null(resolveError);
        Assert.Contains("test.complete", kinds ?? []);

        // Step 1: hosted services list
        var hosted = factory.Services.GetServices<IHostedService>()
            .Select(service => service.GetType().Name)
            .OrderBy(name => name)
            .ToArray();
        Assert.Contains(nameof(ApiOperationWorker), hosted);

        var operation = new ApiOperation
        {
            Kind = "test.complete",
            ApiTokenId = Guid.CreateVersion7(),
            RouteKey = "diag.claim",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            RequestHash = "request-hash"
        };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tokenId = operation.ApiTokenId!.Value;
            var creatorId = Guid.CreateVersion7();
            context.Users.Add(new UserInfo
            {
                Id = creatorId,
                UserName = "diag-seed",
                NormalizedUserName = "DIAG-SEED",
                Email = "diag-seed@example.test",
                NormalizedEmail = "DIAG-SEED@EXAMPLE.TEST",
                EmailConfirmed = true,
                Role = Role.Teacher,
                RegisterTimeUtc = DateTimeOffset.UtcNow
            });
            context.ApiTokens.Add(new ApiTokenEntity
            {
                Id = tokenId,
                Name = "diag",
                CreatorId = creatorId,
                SecretHash = new byte[32],
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            });
            context.ApiOperations.Add(operation);
            await context.SaveChangesAsync();
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var current = await context.ApiOperations.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == operation.Id);
            if (current is not null && current.Status == ApiOperationStatus.Succeeded)
                return;
            await Task.Delay(200);
        }
        throw new TimeoutException("hosted worker did not process the pending operation");
    }
}
