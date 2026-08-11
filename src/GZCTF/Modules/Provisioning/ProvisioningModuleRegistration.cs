using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.Provisioning.Application;
using GZCTF.Modules.Provisioning.Infrastructure;

namespace GZCTF.Modules.Provisioning;

public static class ProvisioningModuleRegistration
{
    public static IServiceCollection AddProvisioningModule(this IServiceCollection services)
    {
        services.AddScoped<IAcademicImportSubmissionStore, EfAcademicImportSubmissionStore>();
        services.AddScoped<AcademicImportApplicationService>();
        services.AddKeyedScoped<IApiOperationHandler, AcademicImportOperationHandler>(
            AcademicImportApplicationService.OperationKind);
        services.AddScoped<IApiOperationResultProvider, AcademicImportResultProvider>();
        services.AddScoped<IApiTokenResourceGrantPolicy, TrainingCourseApiTokenResourceGrantPolicy>();
        services.AddScoped<IApiTokenResourceGrantPolicy, TheoryBankApiTokenResourceGrantPolicy>();
        services.AddScoped<IApiTokenResourceGrantPolicy, TeamApiTokenResourceGrantPolicy>();
        return services;
    }
}
