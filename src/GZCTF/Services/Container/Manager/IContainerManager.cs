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
