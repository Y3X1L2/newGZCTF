using System;
using System.Text.Json;
using System.Threading.Tasks;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.TeamLab.Contracts.Execution;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabDeviceExecutionTests
{
    static readonly string Digest = "sha256:" + new string('a', 64);
    static TeamLabDevicePackage Package() => new()
    {
        Name = "modbus-simulator", Version = "1.0", ArtifactKind = TeamLabDevicePackageArtifactKind.OciImage,
        Digest = Digest, ParameterSchemaJson = """{"type":"object","required":["unit"],"additionalProperties":false,"properties":{"unit":{"type":"integer","minimum":1,"maximum":247}}}""",
        HealthDeclarationJson = """{"kind":"http","port":8080,"path":"/ready"}"""
    };

    [Fact]
    public async Task CompilesValidatedParametersAndDeclaredHealth()
    {
        var result = await TeamLabDeviceExecutionCompiler.CompileAsync(Package(), TeamLabAssetKind.Docker, Digest, """{ "unit": 7 }""", default);
        Assert.Equal("{\"unit\":7}", result.ParametersJson);
        Assert.Equal("/ready", result.HealthPath);
        Assert.Equal(8080, result.HealthPort);
        Assert.True(result.IsValid(Digest));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"unit\":0}")]
    [InlineData("{\"unit\":\"7\"}")]
    [InlineData("{\"unit\":7,\"unknown\":true}")]
    public async Task RejectsParametersBeforeDeployment(string parameters)
    {
        var error = await Assert.ThrowsAsync<TeamLabApiContractException>(() =>
            TeamLabDeviceExecutionCompiler.CompileAsync(Package(), TeamLabAssetKind.Docker, Digest, parameters, default));
        Assert.Equal("device_package_parameters_invalid", error.Code);
    }

    [Fact]
    public async Task RejectsWrongArtifactAndExternalSchemaReferences()
    {
        var error = await Assert.ThrowsAsync<TeamLabApiContractException>(() =>
            TeamLabDeviceExecutionCompiler.CompileAsync(Package(), TeamLabAssetKind.Vm, Digest, "{}", default));
        Assert.Equal("device_package_artifact_mismatch", error.Code);
        var package = Package();
        package.ParameterSchemaJson = """{"$ref":"http://127.0.0.1:1/not-a-schema"}""";
        error = await Assert.ThrowsAsync<TeamLabApiContractException>(() =>
            TeamLabDeviceExecutionCompiler.CompileAsync(package, TeamLabAssetKind.Docker, Digest, "{}", default));
        Assert.Equal("device_package_parameter_schema_invalid", error.Code);
    }

    [Fact]
    public void PlainAssetSerializationDoesNotChangeExistingPlanDigests()
    {
        var asset = new TeamLabAssetExecutionSpecV2("web", "docker", "web", Digest, null, 1, 1, 256, [], []);
        var json = JsonSerializer.Serialize(asset);
        Assert.DoesNotContain("Device", json);
    }

    [Fact]
    public void DeviceResourceMinimumsCannotBeLostDuringPlanning()
    {
        var package = Package();
        package.CpuMillis = 1500;
        package.MemoryMib = 512;
        package.StorageGib = 2;
        var error = Assert.Throws<TeamLabApiContractException>(() =>
            TeamLabDeviceExecutionCompiler.RequireResources(package, new(1, 512, 2048)));
        Assert.Equal("device_package_resources_insufficient", error.Code);
        TeamLabDeviceExecutionCompiler.RequireResources(package, new(2, 512, 2048));
    }
}
