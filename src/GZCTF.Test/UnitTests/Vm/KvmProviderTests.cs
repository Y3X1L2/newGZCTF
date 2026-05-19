using GZCTF.Models.Data;
using GZCTF.Services.Vm;
using Xunit;

namespace GZCTF.Test.UnitTests.Vm;

public class KvmProviderSanitizationTests
{
    [Theory]
    [InlineData("ir-test-vm-1")]
    [InlineData("scenario_42_stage_3")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456789_-")]
    public void SanitizeVmName_AcceptsValidNames(string validName)
    {
        var result = KvmProvider.SanitizeVmName(validName);
        Assert.Equal(validName, result);
    }

    [Theory]
    [InlineData("test;rm -rf /")]
    [InlineData("test|cat /etc/passwd")]
    [InlineData("test`whoami`")]
    public void SanitizeVmName_ThrowsOnShellMetacharacters(string maliciousName)
    {
        Assert.Throws<VmOperationException>(() =>
            KvmProvider.SanitizeVmName(maliciousName));
    }

    [Fact]
    public void SanitizeVmName_ThrowsOnExcessivelyLongName()
    {
        var longName = new string('a', 65);
        Assert.Throws<VmOperationException>(() =>
            KvmProvider.SanitizeVmName(longName));
    }
}
