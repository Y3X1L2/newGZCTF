using System;
using System.Linq;
using GZCTF.Integration.Test.Base;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Ctf.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Integration.Test.Tests.Api.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public sealed class ApiOperationHandlerResolutionTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public void KeyedOperationHandlers_ResolveWithMatchingKinds()
    {
        using var scope = factory.Services.CreateScope();
        var kinds = new[]
        {
            ChallengeExternalApplicationService.OperationKind,
            ImageImportApplicationService.OperationKind,
            BootstrapProfileApplicationService.OperationKind,
            ImageTemplateCertificationService.OperationKind,
            TeamLabRuntimeOperationApplicationService.OperationKind,
            CompletingApiOperationHandler.OperationKind
        };
        foreach (var kind in kinds)
            Assert.Equal(
                kind,
                scope.ServiceProvider.GetRequiredKeyedService<IApiOperationHandler>(kind).Kind);
    }
}
