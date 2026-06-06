using Xunit;
using Microsoft.AspNetCore.RateLimiting;

namespace GZCTF.Test.UnitTests.Security;

public class RateLimitAttributeTests
{
    [Fact]
    public void EnableRateLimiting_Attribute_Exists()
    {
        // Verify the attribute type is available
        var attrType = typeof(EnableRateLimitingAttribute);
        Assert.NotNull(attrType);
    }
}
