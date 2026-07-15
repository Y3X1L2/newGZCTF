namespace GZCTF.Agent.Models;

public sealed record AgentErrorResponse(
    string Category,
    string Code,
    string Message,
    bool Retryable,
    string Operation,
    string CorrelationId);

public sealed class AgentOperationException(
    string category,
    string code,
    string message,
    bool retryable,
    int statusCode = StatusCodes.Status500InternalServerError,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Category { get; } = category;
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
    public int StatusCode { get; } = statusCode;
}

public static class AgentProtocolHeaders
{
    public const string CorrelationId = "X-GZCTF-Correlation-Id";
    public const string ErrorCategory = "X-GZCTF-Error-Category";
    public const string ErrorCode = "X-GZCTF-Error-Code";
    public const string Retryable = "X-GZCTF-Retryable";
}
