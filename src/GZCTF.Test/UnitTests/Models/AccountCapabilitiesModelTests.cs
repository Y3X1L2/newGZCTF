using GZCTF.Models.Internal;
using GZCTF.Models.Request.Account;
using Xunit;

namespace GZCTF.Test.UnitTests.Models;

public class AccountCapabilitiesModelTests
{
    [Fact]
    public void FromConfig_OnlyEnablesPortalWhenEntryUrlIsUsable()
    {
        var disabled = AccountCapabilitiesModel.FromConfig(new(), new PortalSsoConfig
        {
            Enabled = true,
            EntryUrl = "javascript:alert(1)"
        });
        var enabled = AccountCapabilitiesModel.FromConfig(new(), new PortalSsoConfig
        {
            Enabled = true,
            EntryUrl = "http://192.168.20.150:8001/demo/dashboard"
        });

        Assert.False(disabled.PortalSso.Enabled);
        Assert.Null(disabled.PortalSso.EntryUrl);
        Assert.True(enabled.PortalSso.Enabled);
        Assert.Equal("http://192.168.20.150:8001/demo/dashboard", enabled.PortalSso.EntryUrl);
    }

    [Fact]
    public void FromConfig_MapsRegistrationAndRecoveryCapabilities()
    {
        var result = AccountCapabilitiesModel.FromConfig(new AccountPolicy
        {
            AllowRegister = false,
            EmailConfirmationRequired = true
        }, new());

        Assert.True(result.AllowPasswordLogin);
        Assert.False(result.AllowRegister);
        Assert.True(result.PasswordRecoveryAvailable);
        Assert.True(result.EmailConfirmationRequired);
    }
}
