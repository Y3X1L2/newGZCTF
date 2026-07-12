using GZCTF.Modules.Theory.Application;
using GZCTF.Modules.Theory.Infrastructure;

namespace GZCTF.Modules.Theory;

public static class TheoryModuleRegistration
{
    public static IServiceCollection AddTheoryModule(this IServiceCollection services)
    {
        services.AddScoped<ITheoryQuestionCatalog, EfTheoryQuestionCatalog>();
        return services;
    }
}
