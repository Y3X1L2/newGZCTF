using System;
using System.Linq;
using System.Reflection;
using GZCTF.Controllers;
using GZCTF.Models.Request.Game;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabPlayerWorkspaceContractTests
{
    [Fact]
    public void PlayerWorkspaceContract_DoesNotExposeAttackGraphOrFogState()
    {
        var workspaceProperties = typeof(PenetrationWorkspaceModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
        var nodeProperties = typeof(PenetrationWorkspaceNodeModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("AttackGraph", workspaceProperties);
        Assert.DoesNotContain("FogState", nodeProperties);
    }

    [Fact]
    public void PlayerWorkspaceContract_OnlyExposesBlackBoxVpnAndChallengeFields()
    {
        var workspaceProperties = typeof(PenetrationWorkspaceModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
        var nodeProperties = typeof(PenetrationWorkspaceNodeModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("TargetHost", workspaceProperties);
        Assert.DoesNotContain("EntryPoints", workspaceProperties);
        Assert.DoesNotContain("Networks", workspaceProperties);
        Assert.DoesNotContain("Policies", workspaceProperties);
        Assert.DoesNotContain("IpAddress", nodeProperties);
        Assert.DoesNotContain("IsEntry", nodeProperties);
        Assert.DoesNotContain("Interfaces", nodeProperties);
        Assert.DoesNotContain("PositionX", nodeProperties);
        Assert.DoesNotContain("PositionY", nodeProperties);
    }

    [Fact]
    public void PlayerController_DoesNotExposeAttackGraphRoute()
    {
        var routes = typeof(PenetrationPlayerController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>())
            .SelectMany(attr => attr.Template is null ? [] : new[] { attr.Template })
            .ToArray();

        Assert.DoesNotContain(routes, route => route.Contains("attack-graph", StringComparison.OrdinalIgnoreCase));
    }
}
