using GZCTF.Utils;
using Xunit;

namespace GZCTF.Test.UnitTests.Utils;

public class DockerImageReferenceTests
{
    [Theory]
    [InlineData("test-alpine", "docker.io/library/alpine:latest", "", "docker.io/library/alpine:latest")]
    [InlineData("nginx-template", "ghcr.io/example/web:20260613", "", "ghcr.io/example/web:20260613")]
    [InlineData("nginx-template", "https://registry.example.com/team/web:1.0", "", "registry.example.com/team/web:1.0")]
    [InlineData("alpine-template", "alpine:latest", "", "alpine:latest")]
    [InlineData("alpine-template", "nginx", "", "nginx")]
    public void ResolvePullTarget_TreatsImageReferencesAsFullImages(
        string name, string input, string expectedRegistry, string expectedImage)
    {
        var target = DockerImageReference.ResolvePullTarget(name, input);

        Assert.Equal(expectedRegistry, target.RegistryUrl);
        Assert.Equal(expectedImage, target.ImageName);
        Assert.Equal(expectedImage, target.FullImage);
    }

    [Theory]
    [InlineData("busybox:latest", "docker.io", "docker.io", "busybox:latest", "docker.io/busybox:latest")]
    [InlineData("busybox:latest", "docker.io/library", "docker.io/library", "busybox:latest", "docker.io/library/busybox:latest")]
    [InlineData("web:1.0", "10.0.7.120:5000", "10.0.7.120:5000", "web:1.0", "10.0.7.120:5000/web:1.0")]
    [InlineData("web:1.0", "localhost:5000", "localhost:5000", "web:1.0", "localhost:5000/web:1.0")]
    public void ResolvePullTarget_PreservesRegistryOnlyCompatibility(
        string name, string input, string expectedRegistry, string expectedImage, string expectedFullImage)
    {
        var target = DockerImageReference.ResolvePullTarget(name, input);

        Assert.Equal(expectedRegistry, target.RegistryUrl);
        Assert.Equal(expectedImage, target.ImageName);
        Assert.Equal(expectedFullImage, target.FullImage);
    }
}
