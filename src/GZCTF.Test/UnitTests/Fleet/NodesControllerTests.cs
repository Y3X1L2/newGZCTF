using System;
using System.Collections.Generic;
using GZCTF.Controllers;
using GZCTF.Models.Data;
using GZCTF.Services.Fleet;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GZCTF.Test.UnitTests.Fleet;

public class NodesControllerTests
{
    [Fact]
    public void NodeDeployRequest_Defaults()
    {
        var req = new NodeDeployRequest();

        Assert.Equal(string.Empty, req.HostAddress);
        Assert.Equal(string.Empty, req.Username);
        Assert.Equal(string.Empty, req.Password);
        Assert.Null(req.NodeName);
    }

    [Fact]
    public void ResolveServerUrl_PrefersAgentPublicUrl()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:ServerPublicUrl"] = "http://agent-proxy:18083",
                ["Urls"] = "http://internal:18082;http://public:18082"
            })
            .Build();

        Assert.Equal("http://agent-proxy:18083", NodeDeployService.ResolveServerUrl(config));
    }

    [Fact]
    public void ResolveServerUrl_IgnoresBlankAgentPublicUrl()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:ServerPublicUrl"] = " ",
                ["Urls"] = "http://internal:18082;http://public:18082"
            })
            .Build();

        Assert.Equal("http://internal:18082", NodeDeployService.ResolveServerUrl(config));
    }

    [Fact]
    public void BuildAgentConfigJson_UsesListenPortContract()
    {
        var node = new WorkerNode
        {
            Id = Guid.Parse("2e361192-0b30-4244-ad7c-fa7947ea8f41"),
            AuthToken = "token",
            AgentPort = 5101
        };

        var json = NodeDeployService.BuildAgentConfigJson("http://server:18082/", node);

        Assert.Contains("\"ServerUrl\": \"http://server:18082\"", json);
        Assert.Contains("\"NodeId\": \"2e361192-0b30-4244-ad7c-fa7947ea8f41\"", json);
        Assert.Contains("\"AuthToken\": \"token\"", json);
        Assert.Contains("\"ListenPort\": 5101", json);
        Assert.DoesNotContain("AgentPort", json);
    }

    [Fact]
    public void BuildAgentServiceContent_ConfiguresDotnetRoot()
    {
        var content = NodeDeployService.BuildAgentServiceContent("/usr/local/share/dotnet");

        Assert.Contains("Environment=DOTNET_ROOT=/usr/local/share/dotnet", content);
        Assert.Contains("Environment=DOTNET_ROOT_X64=/usr/local/share/dotnet", content);
        Assert.Contains("ExecStart=/usr/local/bin/gzctf-agent", content);
        Assert.Contains("WorkingDirectory=/etc/gzctf-agent", content);
    }

    [Fact]
    public void BuildAgentStartScript_VerifiesEffectiveServiceState()
    {
        var script = NodeDeployService.BuildAgentStartScript("sudo -n");

        Assert.Contains("sudo -n systemctl daemon-reload", script);
        Assert.Contains("sudo -n systemctl enable gzctf-agent >/dev/null 2>&1 || true", script);
        Assert.Contains("sudo -n systemctl restart gzctf-agent", script);
        Assert.DoesNotContain("sudo -n systemctl restart gzctf-agent || true", script);
        Assert.Contains("sudo -n systemctl is-active --quiet gzctf-agent", script);
        Assert.Contains("restart_status=0", script);
        Assert.Contains("systemctl restart exited with ${restart_status}", script);
        Assert.Contains("Agent service did not become active", script);
    }

    [Fact]
    public void BuildAgentVerifyScript_DoesNotAbortBeforeRetries()
    {
        var script = NodeDeployService.BuildAgentVerifyScript("sudo -n", "token", 5101);

        Assert.Contains("for i in $(seq 1 30)", script);
        Assert.Contains("Authorization: Bearer token", script);
        Assert.Contains("http://127.0.0.1:5101/api/status", script);
        Assert.Contains("journalctl -u gzctf-agent.service", script);
        Assert.DoesNotContain("curl -fsS -H 'Authorization: Bearer token' http://127.0.0.1:5101/api/status >/dev/null && exit 0", script);
    }
}
