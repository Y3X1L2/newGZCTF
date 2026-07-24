namespace GZCTF.Agent.Models;

public sealed record StageGuestConformancePackageRequest(
    Guid ProfileId,
    int Version,
    string ArtifactDigest,
    string ArtifactBase64);
