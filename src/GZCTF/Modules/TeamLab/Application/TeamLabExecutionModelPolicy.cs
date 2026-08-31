using GZCTF.Modules.TeamLab.Contracts;

namespace GZCTF.Modules.TeamLab.Application;

internal static class TeamLabExecutionModelPolicy
{
    public static string? FindUnsupportedSecretKey(IEnumerable<TeamLabRuntimeOverlayModel> overlays) =>
        overlays.SelectMany(overlay => overlay.Secrets?.Keys ?? [])
            .FirstOrDefault(key => !IsPlatformSecret(key));

    private static bool IsPlatformSecret(string key) =>
        key.StartsWith("GZCTF_SENSOR_", StringComparison.Ordinal);
}