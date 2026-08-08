using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Ctf.Application;
using GZCTF.Modules.Ctf.Infrastructure;
using GZCTF.Modules.Identity.Application;

namespace GZCTF.Modules.Ctf;

public static class CtfModuleRegistration
{
    public static IServiceCollection AddCtfModule(this IServiceCollection services)
    {
        services.AddScoped<IExternalChallengeCatalog, EfExternalChallengeCatalog>();
        services.AddScoped<IChallengeMutationSubmissionStore, EfChallengeMutationSubmissionStore>();
        services.AddScoped<ChallengeExternalApplicationService>();
        services.AddKeyedScoped<IApiOperationHandler, ChallengeMutationOperationHandler>(
            ChallengeExternalApplicationService.OperationKind);
        services.AddScoped<IApiOperationResultProvider, ChallengeMutationResultProvider>();
        services.AddScoped<IApiTokenResourceGrantPolicy, GameApiTokenResourceGrantPolicy>();
        return services;
    }
}
