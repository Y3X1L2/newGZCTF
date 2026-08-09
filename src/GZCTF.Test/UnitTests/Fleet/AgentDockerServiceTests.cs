using System;
using System.Collections.Generic;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using Xunit;

namespace GZCTF.Test.UnitTests.Fleet;

public class AgentDockerServiceTests
{
    [Fact]
    public void BuildContainerName_IncludesTeamSpecificFingerprint()
    {
        var request = new CreateContainerRequest
        {
            Image = "nginx:alpine",
            ChallengeId = 7,
            UserId = Guid.Empty,
            TeamId = "101",
            ExposedPort = 80,
            Flag = "flag-for-team-101"
        };
        var otherTeam = new CreateContainerRequest
        {
            Image = request.Image,
            ChallengeId = request.ChallengeId,
            UserId = request.UserId,
            TeamId = "102",
            ExposedPort = request.ExposedPort,
            Flag = "flag-for-team-102"
        };

        var name = DockerService.BuildContainerName(request);
        var duplicate = DockerService.BuildContainerName(request);
        var otherName = DockerService.BuildContainerName(otherTeam);

        Assert.Equal(name, duplicate);
        Assert.NotEqual(name, otherName);
        Assert.StartsWith("gzctf_c7_t101_", name);
    }

    [Fact]
    public void MatchesExpectedGeneration_AcceptsManagedLegacyContainerAsGenerationOne()
    {
        var labels = new Dictionary<string, string>
        {
            ["ManagedBy"] = "GZCTF",
            ["ChallengeId"] = "39"
        };

        var matches = DockerService.MatchesExpectedGeneration(labels, 1, out var legacyGeneration);

        Assert.True(matches);
        Assert.True(legacyGeneration);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    public void MatchesExpectedGeneration_RejectsManagedLegacyContainerForLaterGeneration(int generation)
    {
        var labels = new Dictionary<string, string> { ["ManagedBy"] = "GZCTF" };

        var matches = DockerService.MatchesExpectedGeneration(labels, generation, out var legacyGeneration);

        Assert.False(matches);
        Assert.False(legacyGeneration);
    }

    [Fact]
    public void MatchesExpectedGeneration_RejectsUnmanagedContainerWithoutGeneration()
    {
        var labels = new Dictionary<string, string> { ["ManagedBy"] = "other" };

        var matches = DockerService.MatchesExpectedGeneration(labels, 1, out var legacyGeneration);

        Assert.False(matches);
        Assert.False(legacyGeneration);
    }

    [Fact]
    public void MatchesExpectedGeneration_RequiresExactGenerationWhenLabelExists()
    {
        var labels = new Dictionary<string, string>
        {
            ["ManagedBy"] = "GZCTF",
            ["GZCTF.Generation"] = "2"
        };

        Assert.False(DockerService.MatchesExpectedGeneration(labels, 1, out var legacyGeneration));
        Assert.False(legacyGeneration);
        Assert.True(DockerService.MatchesExpectedGeneration(labels, 2, out legacyGeneration));
        Assert.False(legacyGeneration);
    }
}
