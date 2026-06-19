using System.Net;
using GZCTF.Models.Internal;

namespace GZCTF.Services.Container.Manager;

public interface IContainerManager
{
    /// <summary>
    /// Create a container
    /// </summary>
    /// <param name="config">container configuration</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<Models.Data.Container?> CreateContainerAsync(ContainerConfig config, CancellationToken token = default);

    /// <summary>
    /// Destroy a container
    /// </summary>
    /// <param name="container">container</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task DestroyContainerAsync(Models.Data.Container container, CancellationToken token = default);
}

public interface IContainerPatchApplicator
{
    public Task<ContainerPatchApplyResult> ApplyPatchAsync(Models.Data.Container container, Stream archive,
        CancellationToken token = default);
}

public interface IContainerCommandExecutor
{
    public Task<ContainerCommandResult> ExecuteAsync(Models.Data.Container container, IReadOnlyList<string> command,
        TimeSpan timeout, CancellationToken token = default);
}

public interface IPenetrationFabricManager
{
    public bool IsSupported { get; }

    public Task<PenetrationFabricResult> CreateNetworkAsync(string networkName, string cidr,
        CancellationToken token = default);

    public Task<PenetrationFabricResult> AttachInterfaceAsync(Models.Data.Container container,
        PenetrationFabricInterfaceSpec spec, CancellationToken token = default);

    public Task<PenetrationFabricResult> EnableForwardingAsync(Models.Data.Container container,
        CancellationToken token = default);

    public Task<PenetrationFabricResult> ApplyRouteAsync(Models.Data.Container container, string targetCidr,
        string gatewayIp, CancellationToken token = default);

    public Task<PenetrationFabricResult> ProbeAsync(Models.Data.Container container, string targetIp,
        CancellationToken token = default);

    public Task<PenetrationFabricResult> RemoveNetworkAsync(string networkName, CancellationToken token = default);
}

public sealed record PenetrationFabricInterfaceSpec(
    string NetworkName,
    string NetworkCidr,
    string HostInterfaceName,
    string ContainerInterfaceName,
    string IpAddress,
    int PrefixLength,
    bool IsPrimary,
    bool RemoveDefaultRoute);

public sealed record PenetrationFabricResult(
    bool IsSupported,
    bool Succeeded,
    bool TimedOut,
    long? ExitCode,
    string? Message)
{
    public static PenetrationFabricResult Success(string? message = null) =>
        new(true, true, false, 0, message);

    public static PenetrationFabricResult Failed(long? exitCode, string? message) =>
        new(true, false, false, exitCode, message);

    public static PenetrationFabricResult Timeout(string? message = null) =>
        new(true, false, true, null, message);

    public static PenetrationFabricResult Unsupported(string? message = null) =>
        new(false, false, false, null, message);
}

public sealed record ContainerPatchApplyResult(
    bool IsSupported,
    bool Succeeded,
    bool TimedOut,
    long? ExitCode,
    string? Message)
{
    public static ContainerPatchApplyResult Success(string? message = null) =>
        new(true, true, false, 0, message);

    public static ContainerPatchApplyResult Failed(long? exitCode, string? message) =>
        new(true, false, false, exitCode, message);

    public static ContainerPatchApplyResult Timeout(string? message = null) =>
        new(true, false, true, null, message);

    public static ContainerPatchApplyResult Unsupported(string? message = null) =>
        new(false, false, false, null, message);
}

public sealed record ContainerCommandResult(
    bool IsSupported,
    bool Succeeded,
    bool TimedOut,
    long? ExitCode,
    string? Message)
{
    public static ContainerCommandResult Success(string? message = null) =>
        new(true, true, false, 0, message);

    public static ContainerCommandResult Failed(long? exitCode, string? message) =>
        new(true, false, false, exitCode, message);

    public static ContainerCommandResult Timeout(string? message = null) =>
        new(true, false, true, null, message);

    public static ContainerCommandResult Unsupported(string? message = null) =>
        new(false, false, false, null, message);
}

internal static class ContainerManagerLogHelper
{
    private static void LogWithHttpContext<T>(
        ILogger<T> logger,
        string container,
        HttpStatusCode status,
        string body,
        string statusLogFormatKey,
        string responseLogFormatKey
    )
    {
        logger.SystemLog(StaticLocalizer[statusLogFormatKey, container, status],
            TaskStatus.Failed, LogLevel.Warning);
        logger.SystemLog(StaticLocalizer[responseLogFormatKey, container, body],
            TaskStatus.Failed, LogLevel.Error);
    }

    extension<T>(ILogger<T> logger)
    {
        internal void LogCreationFailedWithHttpContext(string container,
            HttpStatusCode status,
            string body
        ) => LogWithHttpContext(logger, container, status, body,
            nameof(Resources.Program.ContainerManager_ContainerCreationFailedStatus),
            nameof(Resources.Program.ContainerManager_ContainerCreationFailedResponse));

        internal void LogDeletionFailedWithHttpContext(string container,
            HttpStatusCode status,
            string body
        ) => LogWithHttpContext(logger, container, status, body,
            nameof(Resources.Program.ContainerManager_ContainerDeletionFailedStatus),
            nameof(Resources.Program.ContainerManager_ContainerDeletionFailedResponse));

        internal void LogServiceCreationFailedWithHttpContext(string container,
            HttpStatusCode status,
            string body
        ) => LogWithHttpContext(logger, container, status, body,
            nameof(Resources.Program.ContainerManager_ServiceCreationFailedStatus),
            nameof(Resources.Program.ContainerManager_ServiceCreationFailedResponse));
    }
}
