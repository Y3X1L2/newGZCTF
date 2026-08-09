using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models.Internal;
using GZCTF.Modules.Content.Application;
using Microsoft.Extensions.Options;
using Xunit;

namespace GZCTF.Test.UnitTests.Content;

public sealed class DockerImageReferencePolicyTests
{
    [Fact]
    public async Task ValidateAsync_AllowsConfiguredInternalRegistry()
    {
        var policy = new DockerImageReferencePolicy(Options.Create(new DockerRegistrySettings
        {
            Address = "10.0.7.118:5000"
        }));

        await policy.ValidateAsync(
            "10.0.7.118:5000/gzctf/nebulamind/edge-gateway:local",
            CancellationToken.None);
    }

    [Fact]
    public async Task ValidateAsync_RejectsOtherPrivateRegistry()
    {
        var policy = new DockerImageReferencePolicy(Options.Create(new DockerRegistrySettings
        {
            Address = "10.0.7.118:5000"
        }));

        var exception = await Assert.ThrowsAsync<ImageReferencePolicyException>(() =>
            policy.ValidateAsync(
                "10.0.7.119:5000/gzctf/untrusted:latest",
                CancellationToken.None));

        Assert.Equal("image_reference_forbidden", exception.Code);
    }
}
