using System.Diagnostics;
using System.Text;
using GZCTF.Models.Internal;
using GZCTF.Repositories.Interface;
using Microsoft.Extensions.Options;

namespace GZCTF.Services.Fleet;

/// <summary>
/// Nginx stream 配置同步服务，定时将容器端口映射写入 Nginx 配置并 reload。
/// 仅在 Linux + NginxProxyConfig.Enable=true 时运行。
/// </summary>
public class NginxSyncService : IHostedService, INginxProxySyncService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NginxProxyConfig _config;
    private readonly ILogger<NginxSyncService> _logger;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private string? _lastConfigHash;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private bool _disposed;

    public NginxSyncService(
        IServiceScopeFactory scopeFactory,
        IOptions<ContainerProvider> containerProvider,
        ILogger<NginxSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = containerProvider.Value.NginxProxyConfig ?? new NginxProxyConfig();
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_config.Enable)
        {
            _logger.LogDebug("NginxSyncService is disabled (NginxProxyConfig.Enable=false)");
            return Task.CompletedTask;
        }

        if (!_config.SyncLocalConfig)
        {
            _logger.LogInformation("Nginx local config sync is disabled; expecting an external gateway to pull mappings");
            return Task.CompletedTask;
        }

        // 仅在 Linux 上运行（nginx reload 需要 Linux 环境）
        if (!OperatingSystem.IsLinux())
        {
            _logger.LogWarning("NginxSyncService is enabled but not running on Linux, skipping");
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = Task.Run(() => SyncLoopAsync(_cts.Token), _cts.Token);

        _logger.LogInformation("NginxSyncService started, sync interval {Interval}s, config path {Path}",
            _config.SyncIntervalSeconds, _config.ConfigPath);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is null)
            return;

        _cts.Cancel();
        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _logger.LogInformation("NginxSyncService stopped");
    }

    async Task SyncLoopAsync(CancellationToken token)
    {
        // 启动后延迟 5 秒，等待平台初始化完成
        await Task.Delay(TimeSpan.FromSeconds(5), token);

        while (!token.IsCancellationRequested)
        {
            try
            {
                await SyncOnceAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Nginx stream sync failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_config.SyncIntervalSeconds), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    async Task SyncOnceAsync(CancellationToken token)
    {
        await _syncLock.WaitAsync(token);
        try
        {
            await SyncOnceCoreAsync(token);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task TrySyncNowAsync(string reason, CancellationToken token = default)
    {
        if (!_config.Enable || !_config.SyncLocalConfig || !OperatingSystem.IsLinux())
            return;

        try
        {
            await SyncOnceAsync(token);
            _logger.LogDebug("Nginx stream config sync requested after {Reason}", reason);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nginx stream sync request failed after {Reason}", reason);
        }
    }

    async Task SyncOnceCoreAsync(CancellationToken token)
    {
        using var scope = _scopeFactory.CreateScope();
        var containerRepo = scope.ServiceProvider.GetRequiredService<IContainerRepository>();
        var portAllocator = scope.ServiceProvider.GetRequiredService<IPortAllocationService>();

        // 获取所有活跃容器的端口映射（远程容器，走 Nginx 代理）
        var mappings = await containerRepo.GetProxyPortMappingsAsync(token);

        // 过滤端口段
        var filteredMappings = mappings
            .Where(m => m.PublicPort >= _config.ListenPortStart
                        && m.PublicPort <= _config.ListenPortEnd)
            .OrderBy(m => m.PublicPort)
            .ToArray();

        foreach (var mapping in filteredMappings)
        {
            if (!await portAllocator.ReserveExistingPortAsync(mapping.PublicPort, mapping.LeaseId, token))
                throw new InvalidOperationException(
                    $"Public port owner conflict for port {mapping.PublicPort}; Nginx sync aborted.");
        }

        // Generate a stream include fragment by default. The main nginx.conf should contain:
        // stream { include /etc/nginx/conf.d/stream-dynamic.conf; }
        // Set WriteStreamBlock=true only when ConfigPath is included at nginx top level.
        var configBuilder = new StringBuilder();
        if (_config.WriteStreamBlock)
            configBuilder.AppendLine("stream {");

        var indent = _config.WriteStreamBlock ? "    " : string.Empty;
        configBuilder.AppendLine($"{indent}map $server_port $upstream_addr {{");
        configBuilder.AppendLine($"{indent}    default 127.0.0.1:1;  # blackhole for unmapped ports");

        foreach (var (publicPort, ip, port, _) in filteredMappings)
        {
            if (!string.IsNullOrEmpty(ip))
                configBuilder.AppendLine($"{indent}    {publicPort} {ip}:{port};");
        }

        configBuilder.AppendLine($"{indent}}}");
        configBuilder.AppendLine($"{indent}server {{");
        configBuilder.AppendLine($"{indent}    listen {_config.ListenPortStart}-{_config.ListenPortEnd};");
        configBuilder.AppendLine($"{indent}    proxy_pass $upstream_addr;");
        configBuilder.AppendLine($"{indent}    proxy_connect_timeout 3s;");
        configBuilder.AppendLine($"{indent}    proxy_timeout 3600s;");
        configBuilder.AppendLine($"{indent}}}");

        if (_config.WriteStreamBlock)
            configBuilder.AppendLine("}");

        var newConfig = configBuilder.ToString();
        var revision = PortMappingRevision.Compute(filteredMappings);
        var leaseIds = filteredMappings.Select(mapping => mapping.LeaseId).ToArray();

        // 计算配置哈希，无变化则跳过
        var newHash = ComputeHash(newConfig);
        if (newHash == _lastConfigHash)
        {
            await containerRepo.SetEntryPublicationResultAsync(
                leaseIds, ContainerEntryStatus.Ready, null, token);
            _logger.LogDebug("Nginx stream config unchanged, skipping reload");
            return;
        }

        var configDirectory = Path.GetDirectoryName(_config.ConfigPath);
        if (!string.IsNullOrWhiteSpace(configDirectory))
            Directory.CreateDirectory(configDirectory);

        // Write a temp file first, then replace the real include path before nginx -t.
        // nginx validates the configured include target, so testing an unreferenced temp file would be a false pass.
        var tempPath = _config.ConfigPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, newConfig, token);
        var backupPath = _config.ConfigPath + ".bak";
        var hadPreviousConfig = File.Exists(_config.ConfigPath);

        if (hadPreviousConfig)
            File.Copy(_config.ConfigPath, backupPath, overwrite: true);

        File.Move(tempPath, _config.ConfigPath, overwrite: true);

        if (!await TestNginxConfigAsync(token))
        {
            _logger.LogError("Nginx config test failed, keeping old config");
            if (hadPreviousConfig)
                File.Move(backupPath, _config.ConfigPath, overwrite: true);
            else
                TryDeleteFile(_config.ConfigPath);
            await containerRepo.SetEntryPublicationResultAsync(
                leaseIds, ContainerEntryStatus.Error, "Public gateway configuration validation failed.", token);
            return;
        }

        // Reload Nginx
        if (await ReloadNginxAsync(token))
        {
            _lastConfigHash = newHash;
            await containerRepo.SetEntryPublicationResultAsync(
                leaseIds, ContainerEntryStatus.Ready, null, token);
            _logger.LogInformation("Nginx stream config reloaded, {Count} port mappings", filteredMappings.Length);
            TryDeleteFile(backupPath);
        }
        else
        {
            await containerRepo.SetEntryPublicationResultAsync(
                leaseIds, ContainerEntryStatus.Error, "Public gateway reload failed.", token);
            if (hadPreviousConfig)
            {
                File.Move(backupPath, _config.ConfigPath, overwrite: true);
                _ = await ReloadNginxAsync(CancellationToken.None);
            }
        }

        _logger.LogDebug("Processed Nginx port map revision {Revision}", revision);
    }

    async Task<bool> TestNginxConfigAsync(CancellationToken token)
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = _config.NginxBinaryPath;
            process.StartInfo.ArgumentList.Add("-t");
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.Start();

            await process.WaitForExitAsync(token);
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(token);
                _logger.LogError("nginx -t failed: {Error}", error.Trim());
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run nginx -t");
            return false;
        }
    }

    async Task<bool> ReloadNginxAsync(CancellationToken token)
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = _config.NginxBinaryPath;
            process.StartInfo.ArgumentList.Add("-s");
            process.StartInfo.ArgumentList.Add("reload");
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.Start();

            await process.WaitForExitAsync(token);
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(token);
                _logger.LogError("nginx -s reload failed: {Error}", error.Trim());
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload nginx");
            return false;
        }
    }

    static string ComputeHash(string content)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(bytes);
    }

    static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch { /* ignore */ }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _syncLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
