using System.Globalization;

namespace GZCTF.DockerRuntime;

internal static class DockerHostPort
{
    public const string Dynamic = "0";

    public static string GetBinding(int? preferred, int? start, int? end, out bool canFallBack)
    {
        canFallBack = false;
        if (preferred is > 0 and <= ushort.MaxValue)
            return preferred.Value.ToString(CultureInfo.InvariantCulture);
        if (start is not > 0 || end is null || end < start || end > ushort.MaxValue)
            return Dynamic;

        canFallBack = true;
        // Docker chooses and claims one port from this range when the container starts.
        return start == end
            ? start.Value.ToString(CultureInfo.InvariantCulture)
            : FormattableString.Invariant($"{start}-{end}");
    }

    public static bool IsAllocationFailure(string? message) => message is not null &&
        (message.Contains("all ports are allocated", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("port is already allocated", StringComparison.OrdinalIgnoreCase) ||
         (message.Contains("failed to bind host port", StringComparison.OrdinalIgnoreCase) &&
          message.Contains("address already in use", StringComparison.OrdinalIgnoreCase)));
}
