using System.Security.Cryptography;
using System.Text;
using GZCTF.Agent.Services;
using GZCTF.Agent.Models;
using GZCTF.TeamLab.Contracts;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace GZCTF.Agent.Services.TeamLab;

internal static class SftpAssetFileStore
{
    internal static Task DownloadToAsync(string address, TeamLabVmFileRequest request, Stream destination,
        long maxBytes, TimeSpan idleTimeout, CancellationToken token) =>
        ExecuteTransferAsync(address, request, idleTimeout,
            (client, ct) => DownloadConnectedAsync(client, request.Path, destination, maxBytes, idleTimeout, ct), token);

    internal static Task UploadFromAsync(string address, TeamLabVmFileRequest request, Stream source,
        long contentLength, long maxBytes, TimeSpan idleTimeout, CancellationToken token) =>
        ExecuteTransferAsync(address, request, idleTimeout,
            (client, ct) => UploadConnectedAsync(client, request, source, contentLength, maxBytes, idleTimeout, ct), token);

    internal static async Task<TeamLabFileResult> ExecuteAsync(string address, TeamLabVmFileRequest request, CancellationToken token)
    {
        if (!TeamLabFileLimits.IsValidPath(request.Path) || request.Port is < 1 or > 65535 ||
            string.IsNullOrWhiteSpace(request.Username) || request.Credential.Length is 0 or > 8192 ||
            request.Operation is not ("probe" or "list" or "download" or "upload" or "delete" or "mkdir" or "move") ||
            request.Operation == "move" && !TeamLabFileLimits.IsValidPath(request.DestinationPath) ||
            request.Content is { Length: > TeamLabFileLimits.MaxBytes })
            throw Failure("files.invalid_request", "SFTP 请求无效。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        token = deadline.Token;
        using var key = request.Credential.StartsWith("-----BEGIN", StringComparison.Ordinal)
            ? new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(request.Credential))) : null;
        AuthenticationMethod authentication = key is null
            ? new PasswordAuthenticationMethod(request.Username, request.Credential)
            : new PrivateKeyAuthenticationMethod(request.Username, key);
        var connection = new Renci.SshNet.ConnectionInfo(address, request.Port, request.Username, authentication) { Timeout = TimeSpan.FromSeconds(10) };
        using var client = new SftpClient(connection) { OperationTimeout = TimeSpan.FromSeconds(15) };
        string? fingerprint = null;
        client.HostKeyReceived += (_, args) =>
        {
            fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(args.HostKey)).TrimEnd('=');
            args.CanTrust = request.HostKeySha256 is null && request.Operation == "probe" || request.HostKeySha256 == fingerprint;
        };
        try
        {
            await client.ConnectAsync(token);
            if (request.Operation == "probe") return new(HostKeySha256: fingerprint);
            var path = request.Path.TrimEnd('/');
            if (path.Length == 0) path = "/";
            if (request.Operation == "list")
            {
                await CleanupStagingAsync(client, path, token);
                var entries = new List<TeamLabFileEntry>();
                await foreach (var entry in client.ListDirectoryAsync(path, token))
                {
                    if (entry.Name is "." or ".." or TeamLabFileLimits.StagingDirectory) continue;
                    if (entries.Count >= TeamLabFileLimits.MaxEntries) throw Failure("files.directory_too_large", "目录超过 1000 项，请选择更小的目录。");
                    entries.Add(new(entry.Name, entry.IsSymbolicLink ? "restricted" : entry.IsDirectory ? "directory" : entry.IsRegularFile ? "file" : "restricted", entry.Length));
                }
                return new(entries.OrderBy(item => item.Kind != "directory").ThenBy(item => item.Name, StringComparer.Ordinal).ToArray(), HostKeySha256: fingerprint);
            }
            if (path == "/") throw Failure("files.invalid_path", "不能修改文件系统根目录。");
            var exists = await client.ExistsAsync(path, token);
            if (!exists && request.Operation is "download" or "delete" or "move") throw Failure("files.not_found", "未找到文件或目录。");
            var attributes = exists ? await client.GetAttributesAsync(path, token) : null;
            if (attributes?.IsSymbolicLink == true) throw Failure("files.restricted", "文件操作不跟随符号链接。");
            if (request.Operation == "mkdir")
            {
                if (exists) throw Failure("files.destination_exists", "目标目录已存在。");
                await client.CreateDirectoryAsync(path, token);
                return new(HostKeySha256: fingerprint);
            }
            if (request.Operation == "move")
            {
                var destination = request.DestinationPath!.TrimEnd('/');
                if (destination.Length == 0) throw Failure("files.invalid_path", "不能替换文件系统根目录。");
                if (await client.ExistsAsync(destination, token) && !request.Overwrite)
                    throw Failure("files.destination_exists", "目标名称已存在。");
                if (request.Overwrite) client.RenameFile(path, destination, isPosix: true);
                else await client.RenameFileAsync(path, destination, token);
                return new(HostKeySha256: fingerprint);
            }
            if (request.Operation == "download")
            {
                using var output = new MemoryStream();
                await DownloadConnectedAsync(client, path, output, TeamLabFileLimits.MaxBytes,
                    TimeSpan.FromSeconds(30), token);
                return new(Content: output.ToArray(), HostKeySha256: fingerprint);
            }
            if (request.Operation == "delete")
            {
                if (attributes?.IsDirectory == true && request.Recursive) await DeleteTreeAsync(client, path, token);
                else if (attributes?.IsDirectory == true) await client.DeleteDirectoryAsync(path, token);
                else if (attributes?.IsRegularFile == true) await client.DeleteFileAsync(path, token);
                else throw Failure("files.unavailable", "只能删除普通文件或空目录。");
                return new(HostKeySha256: fingerprint);
            }
            if (request.Content is null) throw Failure("files.invalid_request", "上传内容不能为空。");
            await using var upload = new MemoryStream(request.Content, writable: false);
            await UploadConnectedAsync(client, request, upload, request.Content.LongLength,
                TeamLabFileLimits.MaxBytes, TimeSpan.FromSeconds(30), token);
            return new(HostKeySha256: fingerprint);
        }
        catch (SshException) when (fingerprint is not null && request.HostKeySha256 is not null && fingerprint != request.HostKeySha256)
        { throw Failure("files.host_key_changed", "虚拟机 SSH 身份已变化，文件连接已停止；请核对虚拟机是否在平台外被替换。"); }
        catch (SshAuthenticationException) { throw Failure("files.authentication_failed", "SSH 运维账号认证失败，请在镜像模板中更新账号配置。"); }
        catch (SftpPathNotFoundException) { throw Failure("files.not_found", "未找到文件或目录。"); }
        catch (SftpPermissionDeniedException) { throw Failure("files.permission_denied", "SSH 账号没有操作此路径的权限。"); }
        catch (SshException) { throw Failure("files.sftp_failed", "SFTP 操作失败，请检查目录权限、目标文件和虚拟机 SSH 服务。"); }
    }

    static async Task ExecuteTransferAsync(string address, TeamLabVmFileRequest request, TimeSpan idleTimeout,
        Func<SftpClient, CancellationToken, Task> operation, CancellationToken token)
    {
        if (!TeamLabFileLimits.IsValidPath(request.Path) || request.Port is < 1 or > 65535 ||
            string.IsNullOrWhiteSpace(request.Username) || request.Credential.Length is 0 or > 8192 ||
            request.HostKeySha256 is null)
            throw Failure("files.invalid_request", "SFTP 请求无效。");
        using var key = request.Credential.StartsWith("-----BEGIN", StringComparison.Ordinal)
            ? new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(request.Credential))) : null;
        AuthenticationMethod authentication = key is null
            ? new PasswordAuthenticationMethod(request.Username, request.Credential)
            : new PrivateKeyAuthenticationMethod(request.Username, key);
        var connection = new Renci.SshNet.ConnectionInfo(address, request.Port, request.Username, authentication)
            { Timeout = TimeSpan.FromSeconds(10) };
        using var client = new SftpClient(connection) { OperationTimeout = idleTimeout };
        string? fingerprint = null;
        client.HostKeyReceived += (_, args) =>
        {
            fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(args.HostKey)).TrimEnd('=');
            args.CanTrust = request.HostKeySha256 == fingerprint;
        };
        try
        {
            await client.ConnectAsync(token);
            await operation(client, token);
        }
        catch (SshException) when (fingerprint is not null && fingerprint != request.HostKeySha256)
        { throw Failure("files.host_key_changed", "虚拟机 SSH 身份已变化，文件连接已停止；请核对虚拟机是否在平台外被替换。"); }
        catch (SshAuthenticationException) { throw Failure("files.authentication_failed", "SSH 运维账号认证失败，请在镜像模板中更新账号配置。"); }
        catch (SftpPathNotFoundException) { throw Failure("files.not_found", "未找到文件或目录。"); }
        catch (SftpPermissionDeniedException) { throw Failure("files.permission_denied", "SSH 账号没有操作此路径的权限。"); }
        catch (SshException) { throw Failure("files.sftp_failed", "SFTP 操作失败，请检查目录权限、目标文件和虚拟机 SSH 服务。"); }
    }

    static async Task DownloadConnectedAsync(SftpClient client, string path, Stream destination,
        long maxBytes, TimeSpan idleTimeout, CancellationToken token)
    {
        path = path.TrimEnd('/');
        if (path.Length == 0) path = "/";
        if (!await client.ExistsAsync(path, token)) throw Failure("files.not_found", "未找到文件或目录。");
        var attributes = await client.GetAttributesAsync(path, token);
        if (!attributes.IsRegularFile || attributes.IsSymbolicLink || attributes.Size > maxBytes)
            throw Failure("files.too_large", "只能下载传输上限内的普通文件。");
        await using var input = await client.OpenAsync(path, FileMode.Open, FileAccess.Read, token);
        await TeamLabFileLimits.CopyAsync(input, destination, maxBytes, idleTimeout, token);
    }

    static async Task UploadConnectedAsync(SftpClient client, TeamLabVmFileRequest request, Stream source,
        long contentLength, long maxBytes, TimeSpan idleTimeout, CancellationToken token)
    {
        if (contentLength < 0 || contentLength > maxBytes) throw Failure("files.too_large", "文件超过传输上限。");
        var path = request.Path.TrimEnd('/');
        if (path.Length == 0) throw Failure("files.invalid_path", "不能替换文件系统根目录。");
        var exists = await client.ExistsAsync(path, token);
        var attributes = exists ? await client.GetAttributesAsync(path, token) : null;
        if (attributes?.IsSymbolicLink == true) throw Failure("files.restricted", "文件操作不跟随符号链接。");
        if (exists && (!request.Overwrite || attributes?.IsRegularFile != true))
            throw Failure("files.destination_exists", "目标已存在；仅在确认后覆盖普通文件。");
        var parent = path[..(path.LastIndexOf('/') + 1)];
        var staging = parent + TeamLabFileLimits.StagingDirectory;
        if (!await client.ExistsAsync(staging, token))
        {
            await client.CreateDirectoryAsync(staging, token);
            client.ChangePermissions(staging, 700);
        }
        var stagingAttributes = await client.GetAttributesAsync(staging, token);
        if (!stagingAttributes.IsDirectory || stagingAttributes.IsSymbolicLink)
            throw Failure("files.restricted", "上传暂存目录无效。");
        await CleanupStagingAsync(client, parent, token);
        var temporary = staging + "/" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var output = await client.OpenAsync(temporary, FileMode.CreateNew, FileAccess.Write, token))
                await TeamLabFileLimits.CopyAsync(source, output, maxBytes, idleTimeout, token);
            if (attributes is not null)
            {
                var updated = await client.GetAttributesAsync(temporary, token);
                updated.UserId = attributes.UserId;
                updated.GroupId = attributes.GroupId;
                updated.OwnerCanRead = attributes.OwnerCanRead; updated.OwnerCanWrite = attributes.OwnerCanWrite; updated.OwnerCanExecute = attributes.OwnerCanExecute;
                updated.GroupCanRead = attributes.GroupCanRead; updated.GroupCanWrite = attributes.GroupCanWrite; updated.GroupCanExecute = attributes.GroupCanExecute;
                updated.OthersCanRead = attributes.OthersCanRead; updated.OthersCanWrite = attributes.OthersCanWrite; updated.OthersCanExecute = attributes.OthersCanExecute;
                client.SetAttributes(temporary, updated);
            }
            token.ThrowIfCancellationRequested();
            if (request.Overwrite) client.RenameFile(temporary, path, isPosix: true);
            else await client.RenameFileAsync(temporary, path, token);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                if (client.IsConnected && await client.ExistsAsync(temporary, cleanup.Token))
                    await client.DeleteFileAsync(temporary, cleanup.Token);
                if (client.IsConnected)
                {
                    try { await client.DeleteDirectoryAsync(staging, cleanup.Token); }
                    catch (SshException) { }
                }
            }
            catch (Exception) when (!client.IsConnected || cleanup.IsCancellationRequested) { }
        }
    }

    static async Task DeleteTreeAsync(SftpClient client, string path, CancellationToken token)
    {
        await foreach (var entry in client.ListDirectoryAsync(path, token))
        {
            if (entry.Name is "." or "..") continue;
            if (entry.IsSymbolicLink) throw Failure("files.restricted", "递归删除不处理符号链接。");
            var child = path.TrimEnd('/') + "/" + entry.Name;
            if (entry.IsDirectory) await DeleteTreeAsync(client, child, token);
            else if (entry.IsRegularFile) await client.DeleteFileAsync(child, token);
            else throw Failure("files.restricted", "递归删除不处理特殊文件。");
        }
        await client.DeleteDirectoryAsync(path, token);
    }

    static async Task CleanupStagingAsync(SftpClient client, string parent, CancellationToken token)
    {
        var staging = parent.TrimEnd('/') + "/" + TeamLabFileLimits.StagingDirectory;
        if (!await client.ExistsAsync(staging, token)) return;
        var attributes = await client.GetAttributesAsync(staging, token);
        if (!attributes.IsDirectory || attributes.IsSymbolicLink) return;
        var count = 0;
        await foreach (var file in client.ListDirectoryAsync(staging, token))
        {
            if (++count > TeamLabFileLimits.MaxEntries) break;
            if (file.IsRegularFile && !file.IsSymbolicLink && Guid.TryParseExact(file.Name, "N", out _) && file.LastWriteTimeUtc < DateTime.UtcNow.AddHours(-1))
                await client.DeleteFileAsync(staging + "/" + file.Name, token);
        }
    }

    static AgentOperationException Failure(string code, string message) => new("FileAccess", code, message, false,
        code switch { "files.not_found" => 404, "files.permission_denied" => 403, "files.too_large" => 413, _ => 409 });
}
