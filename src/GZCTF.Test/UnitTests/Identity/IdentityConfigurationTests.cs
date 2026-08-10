using GZCTF.Extensions.Startup;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace GZCTF.Test.UnitTests.Identity;

public sealed class IdentityConfigurationTests
{
    [Fact]
    public void ConfigureIdentity_UsesStableDataProtectionApplicationName()
    {
        var services = new ServiceCollection();

        IdentityExtension.ConfigureDataProtection(services);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<DataProtectionOptions>>().Value;
        Assert.Equal(IdentityExtension.DataProtectionApplicationName, options.ApplicationDiscriminator);
    }
}
