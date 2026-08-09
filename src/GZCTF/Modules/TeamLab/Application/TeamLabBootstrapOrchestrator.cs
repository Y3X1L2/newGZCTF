using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.Modules.TeamLab.Domain.Runtime;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabBootstrapOrchestrator
{
    public void RecordSuccess(
        TeamLabRuntime runtime,
        TeamLabRuntimeAsset asset,
        TeamLabNodeAssetCreateRequest request,
        TeamLabNodeBootstrapResult result)
    {
        if (request.Bootstrap is null) return;
        var now = DateTimeOffset.UtcNow;
        foreach (var step in result.CompletedSteps)
        {
            var existing = runtime.BootstrapExecutions.SingleOrDefault(item =>
                item.Generation == runtime.Generation && item.AssetId == asset.Id &&
                item.ProfileId == request.Bootstrap.ProfileId && item.ProfileVersion == request.Bootstrap.Version &&
                item.StepKey == step);
            if (existing is null)
            {
                existing = new TeamLabBootstrapExecution
                {
                    RuntimeId = runtime.Id,
                    Generation = runtime.Generation,
                    AssetId = asset.Id,
                    ProfileId = request.Bootstrap.ProfileId,
                    ProfileVersion = request.Bootstrap.Version,
                    StepKey = step
                };
                runtime.BootstrapExecutions.Add(existing);
            }
            existing.Status = TeamLabBootstrapExecutionStatus.Succeeded;
            existing.InputDigest = asset.BootstrapDigest;
            existing.OutputDigest = Digest(step, result.RebootCount, result.PassedHealthChecks);
            existing.LastError = null;
            existing.StartedAt ??= now;
            existing.CompletedAt = now;
        }
    }

    public void RecordFailure(
        TeamLabRuntime runtime,
        TeamLabRuntimeAsset asset,
        TeamLabNodeAssetCreateRequest request,
        string message)
    {
        if (request.Bootstrap is null) return;
        var execution = runtime.BootstrapExecutions.SingleOrDefault(item =>
            item.Generation == runtime.Generation && item.AssetId == asset.Id &&
            item.ProfileId == request.Bootstrap.ProfileId && item.ProfileVersion == request.Bootstrap.Version &&
            item.StepKey == "__profile__");
        if (execution is null)
        {
            execution = new TeamLabBootstrapExecution
            {
                RuntimeId = runtime.Id,
                Generation = runtime.Generation,
                AssetId = asset.Id,
                ProfileId = request.Bootstrap.ProfileId,
                ProfileVersion = request.Bootstrap.Version,
                StepKey = "__profile__"
            };
            runtime.BootstrapExecutions.Add(execution);
        }
        execution.Status = TeamLabBootstrapExecutionStatus.Failed;
        execution.InputDigest = asset.BootstrapDigest;
        execution.LastError = message.Length <= 1024 ? message : message[..1024];
        execution.StartedAt ??= DateTimeOffset.UtcNow;
        execution.CompletedAt = DateTimeOffset.UtcNow;
    }

    static string Digest(string step, int rebootCount, IReadOnlyList<string> healthChecks) =>
        $"sha256:{Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            step,
            rebootCount,
            healthChecks = healthChecks.Order(StringComparer.Ordinal).ToArray()
        })))}";
}
