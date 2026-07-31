namespace GZCTF.Agent.Models;

public sealed record AgentOciRegistryTarget(
    string RegistryAddress,
    string Repository,
    string Tag);

public sealed record CommitVmScenarioRequest(
    Guid OperationId,
    string VmName,
    VmInitOsType OsType,
    string BuildIdentity,
    AgentOciRegistryTarget RegistryTarget);

public sealed record CommitVmScenarioResponse(
    bool Success,
    string ArtifactDigest,
    long ArtifactSize,
    string EvidenceDigest,
    string RegistryAddress,
    string Repository,
    string Tag,
    string? ErrorCode = null,
    string? ErrorDetail = null);
