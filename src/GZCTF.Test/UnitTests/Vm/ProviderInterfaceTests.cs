using GZCTF.Models.Data;
using GZCTF.Services.Vm;
using Xunit;

namespace GZCTF.Test.UnitTests.Vm;

public class VmOperationResultTests
{
    [Fact]
    public void Ok_CreatesSuccessResult()
    {
        var result = VmOperationResult.Ok("test-vm");
        Assert.True(result.Success);
        Assert.Equal("test-vm", result.VmName);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void Fail_CreatesFailureResult()
    {
        var result = VmOperationResult.Fail("test-vm", "disk full");
        Assert.False(result.Success);
        Assert.Equal("disk full", result.ErrorMessage);
    }
}

public class VmConnectionInfoTests
{
    [Fact]
    public void DefaultProtocol_IsVnc()
    {
        var info = new VmConnectionInfo();
        Assert.Equal("vnc", info.Protocol);
    }
}
