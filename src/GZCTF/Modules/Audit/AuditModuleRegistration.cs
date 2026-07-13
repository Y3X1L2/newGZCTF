using GZCTF.Infrastructure.Api;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace GZCTF.Modules.Audit;

public static class AuditModuleRegistration
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services)
    {
        services.AddScoped<IApiOperationStore, EfApiOperationStore>();
        services.AddScoped<IdempotencyService>();
        services.AddScoped<ExternalApiAuditContext>();
        services.AddScoped<OperationalCorrelation>();
        services.AddScoped<ApiOperationService>();
        services.AddScoped<IOperationalEventWriter, EfOperationalEventWriter>();
        services.AddScoped<OperationalEventQueryService>();
        services.AddScoped<AdminMutationAuditFilter>();
        services.AddHostedService<ApiOperationWorker>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ExternalApiAuthorizationResultHandler>();
        return services;
    }
}
