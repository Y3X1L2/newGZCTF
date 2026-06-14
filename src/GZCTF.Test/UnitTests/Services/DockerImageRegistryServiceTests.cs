using System;
using GZCTF.Models.Internal;
using GZCTF.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GZCTF.Test.UnitTests.Services;

public class DockerImageRegistryServiceTests
{
    [Theory]
    [InlineData("10.0.7.130:5000", "ctf", "web/demo", "v1", "10.0.7.130:5000/ctf/web/demo:v1")]
    [InlineData("http://registry.internal:5000", "ctf", "Web/Demo", "latest", "registry.internal:5000/ctf/web/demo:latest")]
    [InlineData("https://registry.internal", "", "team/pwn", "20260614", "registry.internal/team/pwn:20260614")]
    public void BuildInternalImageReference_UsesConfiguredRegistry(
        string address,
        string ns,
        string repository,
        string tag,
        string expected)
    {
        var service = CreateService(address, ns);

        var actual = service.BuildInternalImageReference(repository, tag);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BuildInternalImageReference_RejectsMissingRegistryAddress()
    {
        var service = CreateService("", "ctf");

        Assert.Throws<InvalidOperationException>(() =>
            service.BuildInternalImageReference("web/demo", "v1"));
    }

    [Theory]
    [InlineData("../web")]
    [InlineData("web:demo")]
    [InlineData("web demo")]
    public void BuildInternalImageReference_RejectsInvalidRepository(string repository)
    {
        var service = CreateService("registry.internal:5000", "ctf");

        Assert.Throws<InvalidOperationException>(() =>
            service.BuildInternalImageReference(repository, "v1"));
    }

    static DockerImageRegistryService CreateService(string address, string ns)
        => new(
            Options.Create(new DockerRegistrySettings { Address = address, Namespace = ns }),
            NullLogger<DockerImageRegistryService>.Instance);
}
