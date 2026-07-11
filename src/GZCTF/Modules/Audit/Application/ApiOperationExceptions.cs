namespace GZCTF.Modules.Audit.Application;

public abstract class ApiContractException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed class IdempotencyConflictException()
    : ApiContractException(
        "idempotency_conflict",
        "The Idempotency-Key was already used with a different request payload.",
        409);

public sealed class IdempotencyValidationException(string code, string message)
    : ApiContractException(code, message, 400);

public sealed class ApiOperationAlreadyExistsException : Exception;

public sealed class ApiOperationNotFoundException()
    : ApiContractException("operation_not_found", "The operation was not found.", 404);

public class ApiOperationTerminalException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
