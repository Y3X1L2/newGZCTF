using System;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Integration.Test.Base;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Audit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public sealed class ApiOperationWorkerMinimalTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task Worker_StandaloneStart_ConsumesPendingOperation()
    {
        var worker = ActivatorUtilities.CreateInstance<ApiOperationWorker>(factory.Services);

        var operation = new ApiOperation
        {
            Kind = "test.complete",
            RouteKey = "minimal.worker",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            RequestHash = "request-hash"
        };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<GZCTF.Models.AppDbContext>();
            context.ApiOperations.Add(operation);
            await context.SaveChangesAsync();
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await worker.StartAsync(cts.Token);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(12);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<GZCTF.Models.AppDbContext>();
            var current = await context.ApiOperations.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == operation.Id);
            if (current is not null && current.Status == ApiOperationStatus.Succeeded)
            {
                await worker.StopAsync(CancellationToken.None);
                return;
            }
            await Task.Delay(250);
        }
        await worker.StopAsync(CancellationToken.None);
        throw new TimeoutException("standalone worker did not consume the operation");
    }
}
