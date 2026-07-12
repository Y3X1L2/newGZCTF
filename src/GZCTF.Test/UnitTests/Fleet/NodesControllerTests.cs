using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Controllers;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Concurrency;
using GZCTF.Services.Fleet;
using GZCTF.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
    public void CreatePortPool_ReportsDockerRandomMode_WhenFixedPoolIsNotConfigured()
    {
        var pool = NodesController.CreatePortPool(null, null, "docker", "docker-random");

        Assert.Null(pool.Start);
        Assert.Null(pool.End);
        Assert.Equal(0, pool.Total);
        Assert.Equal("docker-random", pool.Mode);
    }

    [Fact]
    public void CreatePortPool_ReportsConfiguredRange_WhenRangeIsValid()
    {
        var pool = NodesController.CreatePortPool(30000, 30999, "nginx", "nginx-unconfigured");

        Assert.Equal(30000, pool.Start);
        Assert.Equal(30999, pool.End);
        Assert.Equal(1000, pool.Total);
        Assert.Equal("nginx", pool.Mode);
    }

    [Fact]
    public void PortAllocationService_ReportsCurrentNginxAllocationRange()
    {
        var config = new ConfigurationBuilder().Build();
        using var allocator = new PortAllocationService(config,
            Options.Create(new ContainerProvider
            {
                NginxProxyConfig = new NginxProxyConfig
                {
                    Enable = true,
                    ListenPortStart = 30000,
                    ListenPortEnd = 30059
                }
            }),
            NullLogger<PortAllocationService>.Instance);

        Assert.Equal(30000, allocator.CurrentRange.Start);
        Assert.Equal(30059, allocator.CurrentRange.End);
        Assert.Equal("nginx", allocator.CurrentRange.Mode);
    }

    [Fact]
    public void ToNodeVmResource_UsesGameScopedTeamAndResolvedEntry()
    {
        var userId = Guid.Parse("9c0c0dd3-9848-4c85-98ac-0ee12f3c8d3a");
        var challenge = new GameChallenge
        {
            Id = 7,
            Title = "Windows RDP",
            Category = ChallengeCategory.Misc,
            GameId = 20,
            Game = new Game { Id = 20, Title = "Game 20" }
        };
        var vm = new VmInstance
        {
            Id = Guid.Parse("54c2a5f4-a258-4067-87d4-140bf2e95798"),
            UserId = userId,
            ChallengeId = challenge.Id,
            Challenge = challenge,
            VmName = "vm-game-20",
            RdpUrl = "http://guac/#/client/raw",
            GuacamoleConnectionId = "conn-20",
            Status = VmInstanceStatus.Running,
            CreatedAt = DateTimeOffset.Parse("2026-07-02T08:00:00Z")
        };

        var item = NodesController.ToNodeVmResource(
            vm,
            new Dictionary<Guid, string?> { [userId] = "student" },
            new Dictionary<(Guid UserId, int GameId), Team>
            {
                [(userId, 10)] = new Team { Id = 10, Name = "Wrong Game Team" },
                [(userId, 20)] = new Team { Id = 20, Name = "Correct Game Team" }
            },
            "http://guac/#/client/auth?token=resolved");

        Assert.Equal("http://guac/#/client/auth?token=resolved", item.Entry);
        Assert.Equal(20, item.TeamId);
        Assert.Equal("Correct Game Team", item.TeamName);
        Assert.Equal("student", item.UserName);
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
    public void ResolveServerUrl_UsesReachableRequestBaseUrl()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Urls"] = "http://0.0.0.0:18082",
                ["ContainerProvider:PublicEntry"] = "fallback.example.com"
            })
            .Build();

        Assert.Equal("http://10.0.7.118:18082",
            NodeDeployService.ResolveServerUrl(config, "http://10.0.7.118:18082"));
    }

    [Theory]
    [InlineData("http://localhost:18082")]
    [InlineData("http://127.0.0.1:18082")]
    [InlineData("http://0.0.0.0:18082")]
    public void ResolveServerUrl_IgnoresLoopbackRequestBaseUrl(string requestBaseUrl)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Urls"] = "http://server-from-config:18082"
            })
            .Build();

        Assert.Equal("http://server-from-config:18082",
            NodeDeployService.ResolveServerUrl(config, requestBaseUrl));
    }

    [Fact]
    public void ResolveServerUrl_UsesContainerPublicEntryWhenBoundToWildcard()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Urls"] = "http://0.0.0.0:8080",
                ["ContainerProvider:PublicEntry"] = "10.0.7.118"
            })
            .Build();

        Assert.Equal("http://10.0.7.118:8080", NodeDeployService.ResolveServerUrl(config));
    }

    [Fact]
    public void ResolveServerUrl_DoesNotReturnWildcardAddress()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Urls"] = "http://0.0.0.0:8080;http://platform.local:8080"
            })
            .Build();

        Assert.Equal("http://platform.local:8080", NodeDeployService.ResolveServerUrl(config));
    }

    [Fact]
    public void BuildBootstrapScript_InstallsDistributedDependencies()
    {
        var script = NodeDeployService.BuildBootstrapScript();

        Assert.Contains("install_docker", script);
        Assert.Contains("docker info", script);
        Assert.Contains("install_kvm", script);
        Assert.Contains("kvm_ready()", script);
        Assert.Contains("teamlab_tools_ready()", script);
        Assert.Contains("install_missing_pkgs", script);
        Assert.Contains("qemu-system-x86", script);
        Assert.DoesNotContain("apt)\n      install_pkgs qemu-kvm", script);
        Assert.Contains("libvirt", script);
        Assert.Contains("install_teamlab_network_tools", script);
        Assert.Contains("wireguard-tools", script);
        Assert.Contains("nftables", script);
        Assert.Contains("tcpdump", script);
        Assert.Contains("genisoimage", script);
        Assert.Contains("xorriso", script);
        Assert.Contains("cloud-image-utils", script);
        Assert.Contains("cmp -s \"$tmp\" /etc/docker/daemon.json", script);
        Assert.Contains("KVM hardware: unavailable", script);
        Assert.Contains("install_dotnet_runtime", script);
        Assert.Contains("dotnet-install.sh", script);
        Assert.Contains("self-contained", script);
    }

    [Fact]
    public void BuildKvmCapabilityCheckScript_RequiresHardwareVirtualizationAndLibvirt()
    {
        var script = NodeDeployService.BuildKvmCapabilityCheckScript("sudo -n");

        Assert.Contains("test -e /dev/kvm", script);
        Assert.Contains("vmx|svm", script);
        Assert.Contains("sudo -n virsh -c qemu:///system list", script);
        Assert.Contains("NO_KVM_HARDWARE", script);
        Assert.Contains("NO_KVM_LIBVIRT", script);
        Assert.Contains("KVM_OK", script);
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
    public void BuildAgentConfigJson_IncludesTeamLabNetworkMutationConfig()
    {
        var node = new WorkerNode
        {
            Id = Guid.Parse("2e361192-0b30-4244-ad7c-fa7947ea8f41"),
            AuthToken = "token",
            AgentPort = 5101
        };

        var json = NodeDeployService.BuildAgentConfigJson(
            "http://server:18082/",
            node,
            teamLabEnable: true,
            teamLabDryRun: false);

        Assert.Contains("\"TeamLab\"", json);
        Assert.Contains("\"Enable\": true", json);
        Assert.Contains("\"DryRun\": false", json);
    }

    [Fact]
    public void BuildAgentConfigJson_DefaultsToExecutableTeamLabMutation()
    {
        var node = new WorkerNode
        {
            Id = Guid.Parse("2e361192-0b30-4244-ad7c-fa7947ea8f41"),
            AuthToken = "token",
            AgentPort = 5101
        };

        var json = NodeDeployService.BuildAgentConfigJson("http://server:18082/", node);

        using var doc = JsonDocument.Parse(json);
        var teamLab = doc.RootElement.GetProperty("TeamLab");
        Assert.True(teamLab.GetProperty("Enable").GetBoolean());
        Assert.False(teamLab.GetProperty("DryRun").GetBoolean());
    }

    [Fact]
    public void BuildAgentServiceContent_ConfiguresDotnetRoot()
    {
        var content = NodeDeployService.BuildAgentServiceContent("/usr/local/share/dotnet");

        Assert.Contains("Environment=DOTNET_ROOT=/usr/local/share/dotnet", content);
        Assert.Contains("Environment=DOTNET_ROOT_X64=/usr/local/share/dotnet", content);
        Assert.Contains("ExecStart=/usr/local/bin/gzctf-agent", content);
        Assert.Contains("WorkingDirectory=/etc/gzctf-agent", content);
        Assert.Contains("Description=YINYU CTF Agent", content);
    }

    [Fact]
    public void BuildDotnetRootDetectScript_AvoidsMultiLineConditionalParsing()
    {
        var script = NodeDeployService.BuildDotnetRootDetectScript();

        Assert.Contains("command -v dotnet", script);
        Assert.Contains("/usr/share/dotnet/dotnet", script);
        Assert.Contains("readlink -f", script);
        Assert.DoesNotContain("elif", script);
        Assert.DoesNotContain("'", script);
    }

    [Fact]
    public void BuildAgentInstallScript_AvoidsMultiLineConditionalParsing()
    {
        var script = NodeDeployService.BuildAgentInstallScript("http://server/api/agent/download",
            Guid.Parse("2e361192-0b30-4244-ad7c-fa7947ea8f41"), "sudo -n");

        Assert.Contains("wget -q -O \"$tmp\"", script);
        Assert.Contains("curl -fsSL", script);
        Assert.Contains("expected_sha=", script);
        Assert.Contains("sha256sum", script);
        Assert.Contains("Agent binary already matches expected sha256", script);
        Assert.Contains("/tmp/gzctf-agent-2e3611920b304244ad7cfa7947ea8f41", script);
        Assert.DoesNotContain("elif", script);
        Assert.DoesNotContain("\r", script);
    }

    [Fact]
    public void NormalizeShellScript_RemovesWindowsLineEndings()
    {
        var script = NodeDeployService.NormalizeShellScript("one\r\ntwo\rthree");

        Assert.Equal("one\ntwo\nthree", script);
        Assert.DoesNotContain("\r", script);
    }

    [Fact]
    public void BuildAgentStartScript_VerifiesEffectiveServiceState()
    {
        var script = NodeDeployService.BuildAgentStartScript("sudo -n");

        Assert.Contains("sudo -n systemctl daemon-reload", script);
        Assert.Contains("sudo -n systemctl enable gzctf-agent >/dev/null 2>&1 || true", script);
        Assert.Contains("sudo -n systemctl stop gzctf-agent >/dev/null 2>&1 || true", script);
        Assert.Contains("/tmp/gzctf-agent-changed", script);
        Assert.Contains("systemctl is-active --quiet gzctf-agent", script);
        Assert.Contains("pgrep -f '(^|/)(gzctf-agent|GZCTF.Agent|manual-agent)( |$)'", script);
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

    [Fact]
    public async Task RedisDistributedLock_UsesLocalFallback_WhenRedisIsNotConfigured()
    {
        var config = new ConfigurationBuilder().Build();
        using var locker = new RedisDistributedLock(config, NullLogger<RedisDistributedLock>.Instance);

        using var handle = await locker.AcquireAsync("unit-test", TimeSpan.FromSeconds(1));

        Assert.NotNull(handle);
    }

    [Fact]
    public void RedisDistributedLock_FailsClosed_WhenConfiguredRedisIsUnreachable()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:RedisCache"] = "127.0.0.1:1,connectTimeout=50,abortConnect=true"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            new RedisDistributedLock(config, NullLogger<RedisDistributedLock>.Instance));
    }

    [Fact]
    public async Task DeploymentTargetsController_List_DoesNotExposeRawPayloadOrSecrets()
    {
        await using var context = CreateContext();
        context.DeploymentTargets.Add(new DeploymentTarget
        {
            Type = TargetType.Docker,
            Action = TargetAction.Create,
            Status = TargetStatus.Pending,
            Payload = "{\"Flag\":\"flag{secret}\",\"RegistryAuth\":\"token\",\"PrivateKey\":\"wg-private\"}",
            ErrorMessage = "safe error"
        });
        await context.SaveChangesAsync();
        var controller = new DeploymentTargetsController(context,
            new DeploymentQueueService(context, NullLogger<DeploymentQueueService>.Instance),
            new DeploymentQueueViewService(context),
            NullLogger<DeploymentTargetsController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var result = await controller.List();
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);

        Assert.DoesNotContain("Payload", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("flag{secret}", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RegistryAuth", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wg-private", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("safe error", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeploymentTargetsController_List_ParsesDockerCreatePayloadWithStringEnum()
    {
        await using var context = CreateContext();
        context.Games.Add(new Game { Id = 23, Title = "CTF题库" });
        context.Teams.Add(new Team { Id = 18, Name = "whoami" });
        context.GameChallenges.Add(new GameChallenge
        {
            Id = 80,
            GameId = 23,
            Title = "aes90",
            Category = ChallengeCategory.Crypto
        });
        context.DeploymentTargets.Add(new DeploymentTarget
        {
            Type = TargetType.Docker,
            Action = TargetAction.Create,
            Status = TargetStatus.Completed,
            Payload = """
                      {"Image":"10.24.0.28:5000/ctf/pwn/aes1:v1","TeamId":"18","ChallengeId":80,"GameId":23,"UserId":"019ea9c1-8aca-7680-ba85-24b7a9b135d8","ExposedPort":1337,"Flag":"flag{secret}","NetworkMode":"Open","AdditionalNetworkNames":[],"NetworkSubnets":{},"EnvironmentVariables":{},"DnsServers":[],"NetworkAttachments":[]}
                      """
        });
        await context.SaveChangesAsync();
        var controller = new DeploymentTargetsController(context,
            new DeploymentQueueService(context, NullLogger<DeploymentQueueService>.Instance),
            new DeploymentQueueViewService(context),
            NullLogger<DeploymentTargetsController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var result = await controller.List();
        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<DeploymentQueueListResult>(ok.Value);
        var item = Assert.Single(list.Items);
        var json = JsonSerializer.Serialize(ok.Value);

        Assert.Equal("whoami #18", item.OwnerLabel);
        Assert.Equal("CTF题库 #23", item.GameLabel);
        Assert.Equal("aes90 #80", item.ChallengeLabel);
        Assert.Equal("whoami #18 / CTF题库 #23 / aes90 #80", item.RequestLabel);
        Assert.Equal("10.24.0.28:5000/ctf/pwn/aes1:v1", item.Image);
        Assert.DoesNotContain("flag{secret}", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeploymentTargetsController_List_ReturnsStableCursorPage()
    {
        await using var context = CreateContext();
        var baseTime = DateTimeOffset.Parse("2026-07-09T00:00:00Z");
        for (var i = 0; i < 5; i++)
        {
            context.DeploymentTargets.Add(new DeploymentTarget
            {
                Id = Guid.Parse($"00000000-0000-0000-0000-00000000000{i + 1}"),
                Type = TargetType.Docker,
                Action = TargetAction.Create,
                Status = TargetStatus.Completed,
                CreatedAt = baseTime.AddMinutes(i)
            });
        }

        await context.SaveChangesAsync();
        var controller = new DeploymentTargetsController(context,
            new DeploymentQueueService(context, NullLogger<DeploymentQueueService>.Instance),
            new DeploymentQueueViewService(context),
            NullLogger<DeploymentTargetsController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var firstResult = await controller.List(pageSize: 2);
        var firstOk = Assert.IsType<OkObjectResult>(firstResult);
        var firstPage = Assert.IsType<DeploymentQueueListResult>(firstOk.Value);
        Assert.NotNull(firstPage.NextCursor);

        var result = await controller.List(cursor: firstPage.NextCursor, pageSize: 2);
        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<DeploymentQueueListResult>(ok.Value);
        Assert.Equal(
            [
                Guid.Parse("00000000-0000-0000-0000-000000000003"),
                Guid.Parse("00000000-0000-0000-0000-000000000002")
            ],
            list.Items.Select(i => i.Id).ToArray());
    }

    [Fact]
    public async Task DeploymentTargetsController_Cancel_CancelsLinkedQueueTicketAndReleasesReservedCapacity()
    {
        await using var context = CreateContext();
        var node = new WorkerNode
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Name = "node-1",
            HostAddress = "10.24.0.30",
            AuthToken = "token",
            Status = NodeStatus.Online,
            Capabilities = NodeCapability.Docker,
            CurrentContainers = 0,
            ReservedContainers = 1,
            MaxContainers = 2
        };
        var target = new DeploymentTarget
        {
            Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            Type = TargetType.Docker,
            Action = TargetAction.Create,
            Status = TargetStatus.Creating,
            TargetNodeId = node.Id
        };
        var ticket = new DeploymentQueueTicket
        {
            Id = Guid.Parse("66666666-7777-8888-9999-aaaaaaaaaaaa"),
            Kind = DeploymentQueueKind.GameContainer,
            Status = DeploymentQueueTicketStatus.Creating,
            TargetNodeId = node.Id,
            DeploymentTargetId = target.Id,
            DockerSlots = 1,
            ActiveIdentity = "game-container:1:2:3"
        };
        context.WorkerNodes.Add(node);
        context.DeploymentTargets.Add(target);
        context.DeploymentQueueTickets.Add(ticket);
        await context.SaveChangesAsync();
        var capacity = new FleetCapacityReservationService(context,
            new GZCTF.Services.Concurrency.LocalSemaphoreLock(
                NullLogger<GZCTF.Services.Concurrency.LocalSemaphoreLock>.Instance),
            NullLogger<FleetCapacityReservationService>.Instance);
        var queue = new DeploymentQueueService(context, capacity,
            NullLogger<DeploymentQueueService>.Instance);
        var controller = new DeploymentTargetsController(context,
            queue,
            new DeploymentQueueViewService(context),
            NullLogger<DeploymentTargetsController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var result = await controller.Cancel(target.Id);

        Assert.IsType<NoContentResult>(result);
        var reloadedTicket = await context.DeploymentQueueTickets.SingleAsync(t => t.Id == ticket.Id);
        var reloadedTarget = await context.DeploymentTargets.SingleAsync(t => t.Id == target.Id);
        var reloadedNode = await context.WorkerNodes.SingleAsync(n => n.Id == node.Id);
        Assert.Equal(DeploymentQueueTicketStatus.Cancelled, reloadedTicket.Status);
        Assert.Equal(TargetStatus.Cancelled, reloadedTarget.Status);
        Assert.Equal(0, reloadedNode.CurrentContainers);
        Assert.Equal(0, reloadedNode.ReservedContainers);
    }

    [Fact]
    public async Task NodesController_Deregister_CancelsActiveQueueTicketsForRemovedNode()
    {
        await using var context = CreateContext();
        var node = new WorkerNode
        {
            Id = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff"),
            Name = "node-queue",
            HostAddress = "10.24.0.31",
            AuthToken = "token",
            Status = NodeStatus.Online,
            Capabilities = NodeCapability.Docker,
            CurrentContainers = 0,
            MaxContainers = 2
        };
        var ticket = new DeploymentQueueTicket
        {
            Id = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb"),
            Kind = DeploymentQueueKind.GameContainer,
            Status = DeploymentQueueTicketStatus.Creating,
            TargetNodeId = node.Id,
            DockerSlots = 1,
            ActiveIdentity = "game-container:2:3:4"
        };
        context.WorkerNodes.Add(node);
        context.DeploymentQueueTickets.Add(ticket);
        await context.SaveChangesAsync();
        var services = new ServiceCollection()
            .AddSingleton(context)
            .AddLogging()
            .AddSingleton<IDistributedLockService>(
                _ => new LocalSemaphoreLock(NullLogger<LocalSemaphoreLock>.Instance))
            .AddScoped<FleetCapacityReservationService>()
            .BuildServiceProvider();
        var controller = new NodesController(
            new InMemoryNodeRepository(context),
            context,
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ContainerProvider()),
            new PortAllocationService(new ConfigurationBuilder().Build(),
                Options.Create(new ContainerProvider()),
                NullLogger<PortAllocationService>.Instance),
            NullLogger<NodesController>.Instance);

        var result = await controller.Deregister(node.Id);

        Assert.IsType<NoContentResult>(result);
        var reloadedTicket = await context.DeploymentQueueTickets.SingleAsync(t => t.Id == ticket.Id);
        Assert.Equal(DeploymentQueueTicketStatus.Cancelled, reloadedTicket.Status);
        Assert.Null(reloadedTicket.TargetNodeId);
        Assert.Contains("deregistered", reloadedTicket.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Heartbeat_MergesRunningTeamLabAssetsIntoCurrentCapacity()
    {
        await using var context = CreateContext();
        var node = new WorkerNode
        {
            Id = Guid.Parse("cccccccc-dddd-eeee-ffff-aaaaaaaaaaaa"),
            Name = "remote-node",
            HostAddress = "10.24.0.30",
            AuthToken = "node-token",
            Status = NodeStatus.Online,
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            IsLocal = false,
            CurrentContainers = 0,
            ReservedContainers = 2,
            CurrentVms = 0,
            ReservedVms = 1
        };
        var game = new Game { Id = 91, Title = "teamlab" };
        var team = new Team { Id = 92, Name = "teamlab-team" };
        context.WorkerNodes.Add(node);
        context.Games.Add(game);
        context.Teams.Add(team);
        context.TeamLabRuntimes.Add(new TeamLabRuntime
        {
            Id = 93,
            Status = TeamLabRuntimeStatus.Running,
            Assets =
            [
                new TeamLabRuntimeAsset
                {
                    Name = "docker",
                    Kind = TeamLabResourceKind.Docker,
                    WorkerNodeId = node.Id,
                    Status = TeamLabRuntimeStatus.Running
                },
                new TeamLabRuntimeAsset
                {
                    Name = "vm",
                    Kind = TeamLabResourceKind.Vm,
                    WorkerNodeId = node.Id,
                    Status = TeamLabRuntimeStatus.Running
                }
            ]
        });
        await context.SaveChangesAsync();
        var services = new ServiceCollection()
            .AddSingleton(context)
            .AddLogging()
            .AddSingleton<IDistributedLockService>(
                _ => new LocalSemaphoreLock(NullLogger<LocalSemaphoreLock>.Instance))
            .AddScoped<FleetCapacityReservationService>()
            .BuildServiceProvider();
        var controller = new NodesController(
            new InMemoryNodeRepository(context),
            context,
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ContainerProvider()),
            new PortAllocationService(new ConfigurationBuilder().Build(),
                Options.Create(new ContainerProvider()),
                NullLogger<PortAllocationService>.Instance),
            NullLogger<NodesController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.HttpContext.RequestServices = services;
        controller.Request.Headers.Authorization = "Bearer node-token";

        var result = await controller.Heartbeat(node.Id, new HeartbeatRequest
        {
            CpuLoad = 0.1f,
            MemoryLoad = 0.2f,
            CurrentContainers = 1,
            CurrentVms = 0,
            UsedPorts = 3
        });

        Assert.IsType<OkResult>(result);
        var reloaded = await context.WorkerNodes.SingleAsync(n => n.Id == node.Id);
        Assert.Equal(2, reloaded.CurrentContainers);
        Assert.Equal(1, reloaded.CurrentVms);
        Assert.Equal(0, reloaded.ReservedContainers);
        Assert.Equal(0, reloaded.ReservedVms);
    }

    [Fact]
    public async Task Heartbeat_PersistsTeamLabAgentVersionsAndCapabilities()
    {
        await using var context = CreateContext();
        var node = new WorkerNode
        {
            Id = Guid.Parse("dddddddd-eeee-ffff-aaaa-bbbbbbbbbbbb"),
            Name = "remote-node",
            HostAddress = "10.24.0.30",
            AuthToken = "node-token",
            Status = NodeStatus.Online,
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            IsLocal = false,
            TeamLabNetworkEnabled = true
        };
        context.WorkerNodes.Add(node);
        await context.SaveChangesAsync();
        var services = new ServiceCollection()
            .AddSingleton(context)
            .AddLogging()
            .AddSingleton<IDistributedLockService>(
                _ => new LocalSemaphoreLock(NullLogger<LocalSemaphoreLock>.Instance))
            .AddScoped<FleetCapacityReservationService>()
            .BuildServiceProvider();
        var controller = new NodesController(
            new InMemoryNodeRepository(context),
            context,
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ContainerProvider()),
            new PortAllocationService(new ConfigurationBuilder().Build(),
                Options.Create(new ContainerProvider()),
                NullLogger<PortAllocationService>.Instance),
            NullLogger<NodesController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.HttpContext.RequestServices = services;
        controller.Request.Headers.Authorization = "Bearer node-token";

        var result = await controller.Heartbeat(node.Id, new HeartbeatRequest
        {
            CpuLoad = 0.1f,
            MemoryLoad = 0.2f,
            CurrentContainers = 1,
            CurrentVms = 0,
            UsedPorts = 3,
            AgentVersion = "1.8.3-test",
            TeamLabProtocolVersion = 3,
            TeamLabFabricIp = "10.251.0.3",
            TeamLabFabricStatus = TeamLabFabricStatus.Healthy,
            TeamLabCapabilities = new TeamLabNodeCapabilityReport
            {
                Docker = true,
                Kvm = true,
                KvmDevice = true,
                CpuVirtualization = true,
                WireGuard = true,
                Iptables = true,
                Nftables = true,
                Tcpdump = true,
                Dumpcap = false
            }
        });

        Assert.IsType<OkResult>(result);
        var reloaded = await context.WorkerNodes.SingleAsync(n => n.Id == node.Id);
        Assert.Equal("1.8.3-test", reloaded.TeamLabAgentVersion);
        Assert.Equal(3, reloaded.TeamLabProtocolVersion);
        Assert.Equal("10.251.0.3", reloaded.TeamLabFabricIp);
        Assert.Equal(TeamLabFabricStatus.Healthy, reloaded.TeamLabFabricStatus);
        Assert.Equal(NodeCapability.Docker | NodeCapability.Kvm, reloaded.Capabilities);
        Assert.Contains("\"tcpdump\":true", reloaded.TeamLabCapabilitiesJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Heartbeat_DoesNotGrantKvm_WhenDeviceOrCpuVirtualizationIsMissing()
    {
        await using var context = CreateContext();
        var node = new WorkerNode
        {
            Id = Guid.Parse("dddddddd-eeee-ffff-aaaa-bbbbbbbbbbbb"),
            Name = "remote-node",
            HostAddress = "10.24.0.30",
            AuthToken = "node-token",
            Status = NodeStatus.Online,
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            IsLocal = false
        };
        context.WorkerNodes.Add(node);
        await context.SaveChangesAsync();
        var services = new ServiceCollection()
            .AddSingleton(context)
            .AddLogging()
            .AddSingleton<IDistributedLockService>(
                _ => new LocalSemaphoreLock(NullLogger<LocalSemaphoreLock>.Instance))
            .AddScoped<FleetCapacityReservationService>()
            .BuildServiceProvider();
        var controller = new NodesController(
            new InMemoryNodeRepository(context),
            context,
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ContainerProvider()),
            new PortAllocationService(new ConfigurationBuilder().Build(),
                Options.Create(new ContainerProvider()),
                NullLogger<PortAllocationService>.Instance),
            NullLogger<NodesController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.HttpContext.RequestServices = services;
        controller.Request.Headers.Authorization = "Bearer node-token";

        var result = await controller.Heartbeat(node.Id, new HeartbeatRequest
        {
            CpuLoad = 0.1f,
            MemoryLoad = 0.2f,
            CurrentContainers = 1,
            CurrentVms = 0,
            UsedPorts = 3,
            TeamLabCapabilities = new TeamLabNodeCapabilityReport
            {
                Docker = true,
                Kvm = true,
                KvmDevice = false,
                CpuVirtualization = true,
                WireGuard = true,
                Iptables = true,
                Nftables = false,
                Tcpdump = true,
                Dumpcap = false
            }
        });

        Assert.IsType<OkResult>(result);
        var reloaded = await context.WorkerNodes.SingleAsync(n => n.Id == node.Id);
        Assert.Equal(NodeCapability.Docker, reloaded.Capabilities);
    }

    [Fact]
    public async Task List_ReportsTeamLabDockerCapability_WhenKvmIsMissing()
    {
        await using var context = CreateContext();
        context.WorkerNodes.Add(new WorkerNode
        {
            Id = Guid.Parse("eeeeeeee-ffff-aaaa-bbbb-cccccccccccc"),
            Name = "docker-fabric-node",
            HostAddress = "10.24.0.31",
            AuthToken = "node-token",
            Status = NodeStatus.Online,
            LastHeartbeat = DateTimeOffset.UtcNow,
            Capabilities = NodeCapability.Docker,
            IsSchedulable = true,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
            TeamLabTunnelIp = "10.250.0.31",
            TeamLabAgentVersion = "1.8.3-test",
            TeamLabProtocolVersion = 3,
            MaxContainers = 5,
            MaxVms = 0
        });
        await context.SaveChangesAsync();
        var services = new ServiceCollection()
            .AddSingleton(context)
            .AddLogging()
            .BuildServiceProvider();
        var controller = CreateNodesController(context, services);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { RequestServices = services }
        };

        var result = await controller.List();

        var ok = Assert.IsType<OkObjectResult>(result);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var node = Assert.Single(doc.RootElement.EnumerateArray());
        Assert.False(node.GetProperty("CanHostTeamLab").GetBoolean());
        Assert.True(node.GetProperty("CanHostTeamLabFabric").GetBoolean());
        Assert.True(node.GetProperty("CanHostTeamLabDocker").GetBoolean());
        Assert.False(node.GetProperty("CanHostTeamLabVm").GetBoolean());
        var capabilities = node.GetProperty("SchedulableCapabilities").EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        Assert.Contains("TeamLabNetwork", capabilities);
        Assert.Contains("TeamLabDocker", capabilities);
        Assert.DoesNotContain("TeamLabVm", capabilities);
    }

    [Fact]
    public async Task SyncAgent_DelegatesLatestDownloadUrlToAgent()
    {
        await using var context = CreateContext();
        var agentDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "agent");
        Directory.CreateDirectory(agentDir);
        await File.WriteAllBytesAsync(Path.Combine(agentDir, "gzctf-agent"), [1, 2, 3, 4]);
        var nodeId = Guid.Parse("ffffffff-aaaa-bbbb-cccc-dddddddddddd");
        context.WorkerNodes.Add(new WorkerNode
        {
            Id = nodeId,
            Name = "remote-node",
            HostAddress = "10.24.0.31",
            AuthToken = "node-token",
            Status = NodeStatus.Online,
            Capabilities = NodeCapability.Docker,
            AgentPort = 5001
        });
        await context.SaveChangesAsync();
        var agent = new RecordingAgentClient();
        var services = new ServiceCollection()
            .AddSingleton(context)
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton<AgentClient>(agent)
            .AddLogging()
            .BuildServiceProvider();
        var controller = CreateNodesController(context, services);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { RequestServices = services }
        };
        controller.Request.Scheme = "http";
        controller.Request.Host = new HostString("10.24.0.27");

        var result = await controller.SyncAgent(nodeId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("Agent sync requested", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(nodeId, agent.NodeId);
        Assert.Equal("http://10.24.0.27/api/agent/download", agent.Request?.DownloadUrl);
        Assert.True(agent.Request?.Restart);
        Assert.False(string.IsNullOrWhiteSpace(agent.Request?.ExpectedSha256));
    }

    static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new AppDbContext(options);
    }

    static NodesController CreateNodesController(AppDbContext context, IServiceProvider services) =>
        new(
            new InMemoryNodeRepository(context),
            context,
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ContainerProvider()),
            new PortAllocationService(new ConfigurationBuilder().Build(),
                Options.Create(new ContainerProvider()),
                NullLogger<PortAllocationService>.Instance),
            NullLogger<NodesController>.Instance);

    private sealed class InMemoryNodeRepository(AppDbContext context) : INodeRepository
    {
        public Task<List<WorkerNode>> GetOnlineNodesAsync(CancellationToken token) =>
            context.WorkerNodes.Where(n => n.Status == NodeStatus.Online).ToListAsync(token);

        public Task<List<WorkerNode>> GetAllNodesAsync(CancellationToken token) =>
            context.WorkerNodes.ToListAsync(token);

        public Task<WorkerNode?> GetNodeByIdAsync(Guid id, CancellationToken token) =>
            context.WorkerNodes.FirstOrDefaultAsync(n => n.Id == id, token);

        public Task<int> MarkStaleNodesOfflineAsync(TimeSpan timeout, CancellationToken token) =>
            Task.FromResult(0);
    }

    private sealed class RecordingAgentClient : AgentClient
    {
        public RecordingAgentClient() : base(
            new ServiceCollection().AddHttpClient().BuildServiceProvider().GetRequiredService<IHttpClientFactory>(),
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(),
            NullLogger<AgentClient>.Instance)
        {
        }

        public Guid? NodeId { get; private set; }
        public AgentSyncRequest? Request { get; private set; }

        public override Task<AgentSyncResponse> SyncAgentAsync(Guid nodeId, AgentSyncRequest request,
            CancellationToken token)
        {
            NodeId = nodeId;
            Request = request;
            return Task.FromResult(new AgentSyncResponse(true, "Agent sync requested.", "1.8.3-test"));
        }
    }
}
