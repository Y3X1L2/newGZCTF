using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Exercise;
using GZCTF.Modules.Exercise.Application;
using GZCTF.Modules.Provisioning;
using GZCTF.Modules.Provisioning.Application;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Test.UnitTests.Provisioning;

public sealed class AcademicImportRegistrationTests
{
    [Fact]
    public void OperationHandlers_AreRegisteredByOperationKind()
    {
        ServiceCollection services = [];
        services.AddExerciseModule();
        services.AddProvisioningModule();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IApiOperationHandler) &&
            descriptor.IsKeyedService &&
            Equals(descriptor.ServiceKey, ExerciseExternalApplicationService.OperationKind));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IApiOperationHandler) &&
            descriptor.IsKeyedService &&
            Equals(descriptor.ServiceKey, AcademicImportApplicationService.OperationKind));
    }
}
