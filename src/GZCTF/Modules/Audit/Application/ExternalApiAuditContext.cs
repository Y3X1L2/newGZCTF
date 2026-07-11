namespace GZCTF.Modules.Audit.Application;

public sealed class ExternalApiAuditContext
{
    public Guid? OperationId { get; private set; }
    public bool? IdempotencyReused { get; private set; }
    public string? ErrorCode { get; private set; }

    public void SetOperation(Guid operationId, bool reused)
    {
        OperationId = operationId;
        IdempotencyReused = reused;
    }

    public void SetErrorCode(string code) => ErrorCode = code;
}
