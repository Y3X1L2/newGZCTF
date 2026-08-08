using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Integration.Test.Base;
using GZCTF.Modules.Audit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public sealed class ApiOperationWorkerDirectTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task Worker_ManualStart_ClaimsPendingOperation()
    {
        var worker = ActivatorUtilities.CreateInstance<ApiOperationWorker>(factory.Services);

        var operation = new GZCTF.Modules.Audit.Domain.ApiOperation
        {
            Kind = "test.complete",
            RouteKey = "direct.worker",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            RequestHash = "request-hash"
        };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<GZCTF.Models.AppDbContext>();
            context.ApiOperations.Add(operation);
            await context.SaveChangesAsync();
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await worker.StartAsync(cts.Token);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<GZCTF.Models.AppDbContext>();
            var current = await context.ApiOperations.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == operation.Id);
            if (current is not null && current.Status == GZCTF.Modules.Audit.Domain.ApiOperationStatus.Succeeded)
            {
                await worker.StopAsync(CancellationToken.None);
                return;
            }
            await Task.Delay(200);
        }
        await worker.StopAsync(CancellationToken.None);
        throw new TimeoutException("manually started worker did not claim the operation");
    }
}
