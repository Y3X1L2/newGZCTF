using GZCTF.Models.Data;
using GZCTF.Services.Vm;
using Xunit;

namespace GZCTF.Test.UnitTests.Vm;

public class DetectOsTypeTests
{
    [Theory]
    [InlineData("windows-server-2012", OSType.Windows)]
    [InlineData("win10-enterprise", OSType.Windows)]
    [InlineData("winserver2019", OSType.Windows)]
    [InlineData("winsrv-2022-dc", OSType.Windows)]
    [InlineData("wkdb-winserver2012-挖矿病毒模拟", OSType.Windows)]
    [InlineData("WIN2016-STANDARD", OSType.Windows)]
    [InlineData("ubuntu-22.04", OSType.Linux)]
    [InlineData("centos-7", OSType.Linux)]
    [InlineData("debian-kvm", OSType.Linux)]
    [InlineData("unknown-image", OSType.Linux)]
    public void DetectOsType_ReturnsCorrectOsType(string name, OSType expected)
    {
        var result = LocalImageImporter.DetectOsType(name);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void DetectOsType_EmptyString_ReturnsLinux()
    {
        var result = LocalImageImporter.DetectOsType("");
        Assert.Equal(OSType.Linux, result);
    }
}

public class GenerateSafeFileNameTests
{
    [Fact]
    public void GenerateSafeFileName_ProducesUniqueNames()
    {
        var name1 = LocalImageImporter.GenerateSafeFileName("/tmp/test-image.qcow2");
        var name2 = LocalImageImporter.GenerateSafeFileName("/tmp/test-image.qcow2");

        Assert.NotEqual(name1, name2);
    }

    [Fact]
    public void GenerateSafeFileName_PreservesExtension()
    {
        var result = LocalImageImporter.GenerateSafeFileName("/tmp/ubuntu-22.04.qcow2");
        Assert.EndsWith(".qcow2", result);
    }

    [Fact]
    public void GenerateSafeFileName_ContainsOriginalBaseName()
    {
        var result = LocalImageImporter.GenerateSafeFileName("/tmp/ubuntu-22.04.qcow2");
        Assert.Contains("ubuntu-22", result);
    }

    [Fact]
    public void GenerateSafeFileName_OvaExtension()
    {
        var result = LocalImageImporter.GenerateSafeFileName("/images/vm.ova");
        Assert.EndsWith(".ova", result);
    }

    [Fact]
    public void GenerateSafeFileName_NotEmpty()
    {
        var result = LocalImageImporter.GenerateSafeFileName("/tmp/test.qcow2");
        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    [Fact]
    public void GenerateSafeFileName_StripsIllegalChars()
    {
        var result = LocalImageImporter.GenerateSafeFileName("/tmp/test<>:\"/\\|?*.qcow2");
        // Should not contain illegal path characters
        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain(">", result);
        Assert.DoesNotContain(":", result);
        Assert.DoesNotContain("\"", result);
        Assert.DoesNotContain("*", result);
        Assert.DoesNotContain("?", result);
    }
}

public class SupportedExtensionsTests
{
    [Fact]
    public void SupportedExtensions_IncludesQcow2()
    {
        Assert.Contains(".qcow2", LocalImageImporter.SupportedExtensions);
    }

    [Fact]
    public void SupportedExtensions_IncludesOva()
    {
        Assert.Contains(".ova", LocalImageImporter.SupportedExtensions);
    }

    [Fact]
    public void SupportedExtensions_IncludesVmdk()
    {
        Assert.Contains(".vmdk", LocalImageImporter.SupportedExtensions);
    }

    [Fact]
    public void SupportedExtensions_IncludesImg()
    {
        Assert.Contains(".img", LocalImageImporter.SupportedExtensions);
    }
}
