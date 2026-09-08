using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.TeamLab.Contracts;
using Microsoft.EntityFrameworkCore;
using GZCTF.Modules.Content.Application;
using GZCTF.Infrastructure.Concurrency;

namespace GZCTF.Modules.TeamLab.Application;

public interface ITeamLabAssetFileGateway
{
    Task<TeamLabFileResult> ExecuteAsync(Guid nodeId, TeamLabContainerFileRequest request, CancellationToken token);
    Task<TeamLabFileResult> ExecuteVmAsync(Guid nodeId, TeamLabVmFileRequest request, CancellationToken token);
}

public sealed class TeamLabAssetFileService(AppDbContext context, TeamLabAuthorizationService authorization,
    ITeamLabAssetFileGateway gateway, TeamLabEventRecorder events, ImageRemoteAccessService imageAccess,
    IDistributedLeaseProvider leases, TeamLabRuntimeOperationPayloadProtector operationPayloads)
{
    public async Task<TeamLabFileResult> ExecuteAsync(Guid runtimeId, int assetId, Guid actorId, bool administrator,
        TeamLabAssetFileCommand command, CancellationToken token)
    {
        if (!TeamLabFileLimits.IsValidPath(command.Path) || command.Operation is not ("list" or "download" or "upload" or "delete" or "reset-ssh-identity") ||
            command.Content is { Length: > TeamLabFileLimits.MaxBytes } || command.Operation == "upload" && command.Content is null)
            throw new TeamLabApiContractException("files.invalid_request", "路径、操作或文件大小无效；单文件上限为 8 MiB。", 422);
        if ((command.Operation is "delete" or "reset-ssh-identity" || command.Overwrite) && !command.Confirmed)
            throw new TeamLabApiContractException("files.confirmation_required", "删除或覆盖文件需要明确确认。", 422);
        var permission = command.Operation == "reset-ssh-identity" ? TeamLabRuntimePermission.LifecycleManage : TeamLabRuntimePermission.RemoteSessionOperate;
        await authorization.RequirePermissionAsync(runtimeId, actorId, administrator, permission, token);
        await using var lease = await leases.AcquireAsync($"teamlab:asset-files:{runtimeId:D}:{assetId}", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(2), token);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lease.LeaseLost);
        token = linked.Token;
        var asset = await context.TeamLabRuntimeAssets.Include(item => item.Runtime)
            .SingleOrDefaultAsync(item => item.Id == assetId && item.Runtime.PublicId == runtimeId, token)
            ?? throw new TeamLabApiContractException("runtime_asset_not_found", "未找到运行资产。", 404);
        if (asset.Kind is not (TeamLabResourceKind.Docker or TeamLabResourceKind.Vm))
            throw new TeamLabApiContractException("files.unsupported", "当前资产不支持文件管理。", 422);
        if (command.Operation == "reset-ssh-identity")
        {
            if (asset.Kind != TeamLabResourceKind.Vm) throw new TeamLabApiContractException("files.unsupported", "只有虚拟机使用 SSH 身份登记。", 422);
        }
        if (asset.Generation != command.Generation || asset.Runtime.Generation != command.Generation ||
            asset.Runtime.Status is not (TeamLabRuntimeStatus.Running or TeamLabRuntimeStatus.Failed) || asset.WorkerNodeId is null || string.IsNullOrWhiteSpace(asset.RuntimeResourceId))
            throw new TeamLabApiContractException("files.unavailable", "资产代次已变化或运行环境尚不可用，请刷新。", 409);
        if (await TeamLabAssetControlGuard.IsActiveAsync(context, operationPayloads, asset.RuntimeId, assetId, token))
            throw new TeamLabApiContractException("files.asset_busy", "该资产正在执行生命周期操作，请等待完成。", 409);
        TeamLabFileResult result;
        if (asset.Kind == TeamLabResourceKind.Docker)
            result = await gateway.ExecuteAsync(asset.WorkerNodeId.Value, new(asset.RuntimeId, command.Generation,
                asset.RuntimeResourceId, command.Operation, command.Path, command.Content, command.Overwrite), token);
        else
        {
            var configuration = await context.ImageTemplateRemoteAccesses.AsNoTracking().SingleOrDefaultAsync(item => item.ImageTemplateId == asset.SourceTemplateId, token);
            if (configuration is not { Enabled: true, Protocol: TeamLabRemoteProtocol.Ssh } || string.IsNullOrWhiteSpace(configuration.Username) ||
                string.IsNullOrWhiteSpace(configuration.ProtectedSecret) || !Guid.TryParse(asset.NativeIdentity, out var identity) || string.IsNullOrWhiteSpace(asset.IpAddress))
                throw new TeamLabApiContractException("files.ssh_configuration_missing", "VM 文件管理需要镜像已配置 SSH 运维账号，并已取得虚拟机执行身份和地址。", 409);
            var request = new TeamLabVmFileRequest(asset.RuntimeResourceId, command.Generation, identity, asset.IpAddress,
                configuration.Port, configuration.Username, imageAccess.RevealSecret(configuration), command.Operation,
                command.Path, command.Content, command.Overwrite, asset.SftpHostKeySha256);
            if (asset.SftpHostKeySha256 is null || command.Operation == "reset-ssh-identity")
            {
                var probe = await gateway.ExecuteVmAsync(asset.WorkerNodeId.Value, request with { Operation = "probe", Content = null, HostKeySha256 = null }, token);
                if (probe.HostKeySha256 is not { Length: > 7 and <= 96 } fingerprint || !fingerprint.StartsWith("SHA256:", StringComparison.Ordinal))
                    throw new TeamLabApiContractException("files.host_key_unavailable", "SSH 服务没有返回有效的身份摘要。", 409);
                asset.SftpHostKeySha256 = fingerprint;
                await context.SaveChangesAsync(token);
                request = request with { HostKeySha256 = fingerprint };
            }
            result = command.Operation == "reset-ssh-identity" ? new TeamLabFileResult(HostKeySha256: asset.SftpHostKeySha256)
                : await gateway.ExecuteVmAsync(asset.WorkerNodeId.Value, request, token);
        }
        if (!await context.TeamLabRuntimeAssets.AnyAsync(item => item.Id == assetId && item.Generation == command.Generation &&
                item.Runtime.Generation == command.Generation && item.RuntimeResourceId == asset.RuntimeResourceId && item.WorkerNodeId == asset.WorkerNodeId, token))
            throw new TeamLabApiContractException("files.stale_generation", "操作期间资产绑定已变化，请刷新后核对结果。", 409);
        await authorization.RequirePermissionAsync(runtimeId, actorId, administrator, permission, token);
        events.Record(asset.Runtime, "asset-files", TeamLabEventLevel.Info,
            OperationalEventCodes.TeamLab.AssetFilesAccessed, OperationalEventOutcome.Succeeded,
            "资产文件操作完成", workerNodeId: asset.WorkerNodeId, detail: new Dictionary<string, object?>
            {
                ["assetId"] = assetId, ["actorUserId"] = actorId, ["generation"] = command.Generation,
                ["operation"] = command.Operation,
                ["filePath"] = command.Path
            });
        await context.SaveChangesAsync(token);
        return result;
    }
}
