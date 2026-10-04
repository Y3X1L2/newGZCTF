using System.Linq.Expressions;
using GZCTF.Models;
using GZCTF.Modules.Audit.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Base;

public static class ExternalApiAuditAssertions
{
    public static async Task AssertPersistedAsync(IServiceProvider services,
        Expression<Func<ExternalApiRequestAudit, bool>> predicate)
    {
        // Request audit persistence is buffered; an HTTP response only confirms queue admission.
        for (var attempt = 0; attempt < 50; attempt++)
        {
            await using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await context.ExternalApiRequestAudits.AsNoTracking().AnyAsync(predicate)) return;
            await Task.Delay(100);
        }
        Assert.Fail("The expected external API request audit was not persisted within five seconds.");
    }
}
