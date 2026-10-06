using GZCTF.Models.Data;

namespace GZCTF.Modules.Content.Contracts;

public sealed record ImageRuntimeAccessSummary(
    int TemplateId,
    string? ImageHash,
    OSType OperatingSystem,
    string? RemoteProtocol,
    bool RemoteConfigured);
