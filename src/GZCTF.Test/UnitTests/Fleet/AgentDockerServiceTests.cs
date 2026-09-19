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
    public void MatchesExpectedGeneration_RejectsContainerWithoutGeneration()
    {
        var labels = new Dictionary<string, string>
        {
            ["ManagedBy"] = "GZCTF",
            ["ChallengeId"] = "39"
        };

        var matches = DockerService.MatchesExpectedGeneration(labels, 1);

        Assert.False(matches);
    }

    [Fact]
    public void MatchesExpectedGeneration_RejectsUnmanagedContainerWithoutGeneration()
    {
        var labels = new Dictionary<string, string> { ["ManagedBy"] = "other" };

        var matches = DockerService.MatchesExpectedGeneration(labels, 1);

        Assert.False(matches);
    }

    [Fact]
    public void MatchesExpectedGeneration_RequiresExactGenerationWhenLabelExists()
    {
        var labels = new Dictionary<string, string>
        {
            ["ManagedBy"] = "GZCTF",
            ["GZCTF.Generation"] = "2"
        };

        Assert.False(DockerService.MatchesExpectedGeneration(labels, 1));
        Assert.True(DockerService.MatchesExpectedGeneration(labels, 2));
    }
}
