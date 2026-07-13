using GZCTF.Modules.Audit.Domain;

namespace GZCTF.Modules.Audit.Contracts;

public sealed record OperationalError(
    OperationalErrorCategory Category,
    string Code,
    string Message,
    bool Retryable,
    int? HttpStatus = null,
    Guid? WorkerNodeId = null,
    string? Operation = null);

public sealed record AgentErrorResponse(
    string Category,
    string Code,
    string Message,
    bool Retryable,
    string Operation,
    string CorrelationId);

public static class OperationalErrorCodes
{
    public const string AuthorizationForbidden = "auth.forbidden";
    public const string RequestInvalid = "request.invalid";
    public const string OperationConflict = "operation.conflict";
    public const string RuntimeIdentityConflict = "runtime.identity_conflict";
    public const string RuntimeResourceMissing = "runtime.resource_missing";
    public const string RuntimeNoEligibleNode = "runtime.no_eligible_node";
    public const string RuntimeCapacityExhausted = "runtime.capacity_exhausted";
    public const string ImageRegistryUnreachable = "image.registry_unreachable";
    public const string ImageRegistryFailed = "image.registry_failed";
    public const string ImageArtifactMissing = "image.artifact_missing";
    public const string ImageTransferFailed = "image.transfer_failed";
    public const string ImageTransferTimeout = "image.transfer_timeout";
    public const string ImageDigestMismatch = "image.digest_mismatch";
    public const string ImageCleanupFailed = "image.cleanup_failed";
    public const string NodeNotFound = "node.not_found";
    public const string NodeOffline = "node.offline";
    public const string AgentFeatureMissing = "agent.feature_missing";
    public const string AgentResponseInvalid = "agent.response_invalid";
    public const string AgentSyncFailed = "agent.sync_failed";
    public const string AgentTimeout = "agent.timeout";
    public const string AgentConnectionFailed = "agent.connection_failed";
    public const string DockerOperationFailed = "docker.operation_failed";
    public const string KvmOperationFailed = "kvm.operation_failed";
    public const string NetworkOperationFailed = "network.operation_failed";
    public const string HealthProbeTimeout = "health.probe_timeout";
    public const string StorageUnavailable = "storage.unavailable";
    public const string StorageFileNotFound = "storage.file_not_found";
    public const string DatabaseUnavailable = "database.unavailable";
    public const string CacheUnavailable = "cache.unavailable";
    public const string UnclassifiedFailure = "operation.unclassified_failure";
}

public static class OperationalErrorClassifier
{
    public static OperationalError FromException(
        Exception exception,
        string operation,
        Guid? workerNodeId = null)
    {
        return exception switch
        {
            OperationCanceledException => new OperationalError(
                OperationalErrorCategory.AgentTransport,
                OperationalErrorCodes.AgentTimeout,
                "The Agent operation timed out.",
                true,
                WorkerNodeId: workerNodeId,
                Operation: operation),
            HttpRequestException => new OperationalError(
                OperationalErrorCategory.AgentTransport,
                OperationalErrorCodes.AgentConnectionFailed,
                "The Agent connection failed.",
                true,
                WorkerNodeId: workerNodeId,
                Operation: operation),
            ArgumentException => new OperationalError(
                OperationalErrorCategory.Validation,
                OperationalErrorCodes.RequestInvalid,
                exception.Message,
                false,
                WorkerNodeId: workerNodeId,
                Operation: operation),
            _ => new OperationalError(
                OperationalErrorCategory.Unknown,
                OperationalErrorCodes.UnclassifiedFailure,
                exception.Message,
                false,
                WorkerNodeId: workerNodeId,
                Operation: operation)
        };
    }

    public static OperationalError FromHttpStatus(
        int statusCode,
        string operation,
        string message,
        Guid? workerNodeId = null)
    {
        return statusCode switch
        {
            400 or 422 => new OperationalError(
                OperationalErrorCategory.Validation,
                OperationalErrorCodes.RequestInvalid,
                message,
                false,
                statusCode,
                workerNodeId,
                operation),
            401 or 403 => new OperationalError(
                OperationalErrorCategory.Authorization,
                OperationalErrorCodes.AuthorizationForbidden,
                message,
                false,
                statusCode,
                workerNodeId,
                operation),
            404 => new OperationalError(
                OperationalErrorCategory.AgentProtocol,
                OperationalErrorCodes.AgentFeatureMissing,
                message,
                false,
                statusCode,
                workerNodeId,
                operation),
            409 => new OperationalError(
                OperationalErrorCategory.Conflict,
                OperationalErrorCodes.OperationConflict,
                message,
                false,
                statusCode,
                workerNodeId,
                operation),
            408 or 429 or >= 500 => new OperationalError(
                OperationalErrorCategory.AgentTransport,
                OperationalErrorCodes.AgentConnectionFailed,
                message,
                true,
                statusCode,
                workerNodeId,
                operation),
            _ => new OperationalError(
                OperationalErrorCategory.Unknown,
                OperationalErrorCodes.UnclassifiedFailure,
                message,
                false,
                statusCode,
                workerNodeId,
                operation)
        };
    }

    public static OperationalError FromAgentResponse(
        AgentErrorResponse? response,
        int statusCode,
        string operation,
        string fallbackMessage,
        Guid? workerNodeId = null)
    {
        if (response is null ||
            !Enum.TryParse<OperationalErrorCategory>(response.Category, true, out var category) ||
            string.IsNullOrWhiteSpace(response.Code))
            return FromHttpStatus(statusCode, operation, fallbackMessage, workerNodeId);

        var message = string.IsNullOrWhiteSpace(response.Message)
            ? fallbackMessage
            : $"{fallbackMessage} {response.Message.Trim()}";
        return new OperationalError(
            category,
            response.Code.Trim(),
            message.Length <= 1024 ? message : message[..1024],
            response.Retryable,
            statusCode,
            workerNodeId,
            operation);
    }
}
