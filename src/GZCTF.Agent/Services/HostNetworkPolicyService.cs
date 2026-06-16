using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using GZCTF.Agent.Models;

namespace GZCTF.Agent.Services;

public class HostNetworkPolicyService(ILogger<HostNetworkPolicyService> logger)
{
    const string DockerUserChain = "DOCKER-USER";
    const int CommandTimeoutSeconds = 8;

    public async Task<(bool Success, string Message)> ApplyAsync(NetworkPolicySetRequest policySet,
        CancellationToken token = default)
    {
        if (policySet.Rules.Count == 0)
            return await RemoveAsync(policySet.SetName, token);

        var supported = await CheckSupportedAsync(token);
        if (!supported.Success)
            return supported;

        var chain = BuildChainName(policySet.SetName);
        var clean = await RemoveChainAsync(chain, policySet.SetName, token);
        if (!clean.Success)
            return clean;

        var create = await RunIptablesAsync(["-N", chain], token);
        if (!create.Success)
            return (false, $"创建访问策略链失败：{create.Message}");

        var jump = await RunIptablesAsync(
            ["-I", DockerUserChain, "1", "-m", "comment", "--comment", BuildJumpComment(policySet.SetName), "-j", chain],
            token);
        if (!jump.Success)
        {
            await RemoveChainAsync(chain, policySet.SetName, token);
            return (false, $"挂载访问策略链失败：{jump.Message}");
        }

        var established = await RunIptablesAsync(
            ["-A", chain, "-m", "conntrack", "--ctstate", "RELATED,ESTABLISHED", "-j", "ACCEPT"], token);
        if (!established.Success)
        {
            await RemoveChainAsync(chain, policySet.SetName, token);
            return (false, $"写入连接保持规则失败：{established.Message}");
        }

        foreach (var rule in policySet.Rules)
        {
            foreach (var args in BuildRuleArguments(chain, rule))
            {
                var result = await RunIptablesAsync(args, token);
                if (result.Success)
                    continue;

                await RemoveChainAsync(chain, policySet.SetName, token);
                return (false, $"写入访问策略“{rule.Comment}”失败：{result.Message}");
            }
        }

        var tail = await RunIptablesAsync(["-A", chain, "-j", "RETURN"], token);
        if (!tail.Success)
        {
            await RemoveChainAsync(chain, policySet.SetName, token);
            return (false, $"写入策略链返回规则失败：{tail.Message}");
        }

        logger.LogInformation("Applied penetration network policy set {SetName} with {RuleCount} rules",
            policySet.SetName, policySet.Rules.Count);
        return (true, $"已下发 {policySet.Rules.Count} 条访问控制规则");
    }

    public async Task<(bool Success, string Message)> RemoveAsync(string setName, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(setName))
            return (true, "策略集为空，无需清理。");

        var supported = await CheckSupportedAsync(token);
        if (!supported.Success)
            return (true, "当前节点不支持主机访问策略，跳过清理。");

        var result = await RemoveChainAsync(BuildChainName(setName), setName, token);
        return result.Success ? (true, "访问控制规则已清理") : result;
    }

    async Task<(bool Success, string Message)> CheckSupportedAsync(CancellationToken token)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return (false, "Windows Docker 主机暂不支持渗透访问策略自动下发。");

        var result = await RunIptablesAsync(["--version"], token, logFailure: false);
        return result.Success
            ? (true, "iptables available")
            : (false, "当前节点无法执行 iptables，请在 Linux Docker Worker 上部署渗透环境。");
    }

    async Task<(bool Success, string Message)> RemoveChainAsync(string chain, string setName, CancellationToken token)
    {
        for (var i = 0; i < 8; i++)
        {
            var deleteJump = await RunIptablesAsync(
                ["-D", DockerUserChain, "-m", "comment", "--comment", BuildJumpComment(setName), "-j", chain],
                token, logFailure: false);
            if (!deleteJump.Success)
                break;
        }

        await RunIptablesAsync(["-F", chain], token, logFailure: false);
        var deleteChain = await RunIptablesAsync(["-X", chain], token, logFailure: false);
        if (!deleteChain.Success && !deleteChain.Message.Contains("No chain", StringComparison.OrdinalIgnoreCase) &&
            !deleteChain.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
            return (false, $"清理旧访问策略链失败：{deleteChain.Message}");

        return (true, "旧访问策略链已清理。");
    }

    static IEnumerable<string[]> BuildRuleArguments(string chain, NetworkPolicyRuleRequest rule)
    {
        var protocols = ExpandProtocols(rule.Protocol, HasPort(rule.PortRange));
        foreach (var protocol in protocols)
        {
            var args = new List<string> { "-A", chain, "-s", rule.Source, "-d", rule.Target };
            if (!string.Equals(protocol, "any", StringComparison.OrdinalIgnoreCase))
                args.AddRange(["-p", protocol]);

            if (HasPort(rule.PortRange) && (protocol == "tcp" || protocol == "udp"))
            {
                var port = NormalizePortRange(rule.PortRange);
                if (port.Contains(',', StringComparison.Ordinal))
                    args.AddRange(["-m", "multiport", "--dports", port]);
                else
                    args.AddRange(["--dport", port]);
            }

            if (!string.IsNullOrWhiteSpace(rule.Comment))
                args.AddRange(["-m", "comment", "--comment", TrimComment(rule.Comment)]);

            args.AddRange(["-j", rule.Allow ? "ACCEPT" : "DROP"]);
            yield return args.ToArray();
        }
    }

    static string[] ExpandProtocols(string protocol, bool hasPort)
    {
        var normalized = protocol.Trim().ToLowerInvariant();
        return normalized switch
        {
            "tcp" => ["tcp"],
            "udp" => ["udp"],
            "icmp" => ["icmp"],
            _ => hasPort ? ["tcp", "udp"] : ["any"]
        };
    }

    static bool HasPort(string? portRange) =>
        !string.IsNullOrWhiteSpace(portRange) &&
        !portRange.Equals("any", StringComparison.OrdinalIgnoreCase) &&
        portRange != "*";

    static string NormalizePortRange(string portRange) =>
        portRange.Trim().Replace('-', ':');

    static string TrimComment(string value) =>
        value.Length <= 128 ? value : value[..128];

    static string BuildJumpComment(string setName) =>
        $"GZCTF-PENTEST:{setName}";

    static string BuildChainName(string setName)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(setName)))
            .ToLowerInvariant()[..20];
        return $"GZPT{hash}";
    }

    static async Task<(bool Success, string Message)> RunIptablesAsync(string[] args, CancellationToken token,
        bool logFailure = true)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(CommandTimeoutSeconds));

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "iptables",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var arg in args)
                process.StartInfo.ArgumentList.Add(arg);

            var output = new StringBuilder();
            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                    output.AppendLine(eventArgs.Data);
            };
            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                    output.AppendLine(eventArgs.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(timeout.Token);

            return (process.ExitCode == 0, output.ToString().Trim());
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return (false, "iptables 执行超时。");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
