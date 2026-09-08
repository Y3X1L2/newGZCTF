using System;
using GZCTF.Extensions.Startup;
using Xunit;

namespace GZCTF.Test.UnitTests.Services;

public class DatabaseStartupLoggingTests
{
    [Fact]
    public void FailureCategoryDoesNotIncludeConfigurationOrInnerException()
    {
        var exception = new InvalidOperationException("Password=must-not-appear",
            new Exception("Token=also-must-not-appear"));
        Assert.Equal("InvalidOperationException", DatabaseExtension.FailureCategory(exception));
    }
}
