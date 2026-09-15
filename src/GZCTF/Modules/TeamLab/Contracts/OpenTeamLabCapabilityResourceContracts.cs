using System.Text.Json;

namespace GZCTF.Modules.TeamLab.Contracts;

public sealed record OpenRegisterTeamLabDevicePackageModel(
    string Name,
    string DisplayName,
    string Version,
    string ArtifactKind,
    string ArtifactReference,
    string? Digest,
    string? Description,
    IReadOnlyList<string>? SupportedAssetKinds,
    int CpuMillis,
    int MemoryMib,
    int StorageGib,
    IReadOnlyList<TeamLabDevicePackagePortModel>? Ports,
    JsonElement? ParameterSchema,
    JsonElement? HealthDeclaration,
    IReadOnlyList<string>? ProtocolEventTypes);

public sealed record OpenUpdateTeamLabDevicePackageModel(
    string Name,
    string DisplayName,
    string Version,
    string ArtifactKind,
    string ArtifactReference,
    string? Digest,
    string? Description,
    IReadOnlyList<string>? SupportedAssetKinds,
    int CpuMillis,
    int MemoryMib,
    int StorageGib,
    IReadOnlyList<TeamLabDevicePackagePortModel>? Ports,
    JsonElement? ParameterSchema,
    JsonElement? HealthDeclaration,
    IReadOnlyList<string>? ProtocolEventTypes);

public sealed record OpenRegisterTeamLabConnectorModel(
    string Name,
    string DisplayName,
    string Kind,
    Guid? ControlScopeId,
    bool SupportsSharedUse,
    int Capacity,
    string? AttachmentReference,
    string? Description,
    TeamLabManagedNicModel? ManagedNic = null);

public sealed record OpenUpdateTeamLabConnectorModel(
    string Name,
    string DisplayName,
    string Kind,
    Guid? ControlScopeId,
    bool SupportsSharedUse,
    int Capacity,
    string? AttachmentReference,
    string? Description,
    TeamLabManagedNicModel? ManagedNic = null);

public sealed record OpenTeamLabConnectorHealthModel(
    Guid ConnectorId,
    string Health,
    DateTimeOffset? ObservedAt);

public static class OpenTeamLabCapabilityResourceContractMapper
{
    public static RegisterTeamLabDevicePackageModel ToInternal(this OpenRegisterTeamLabDevicePackageModel model) =>
        new(model.Name, model.DisplayName, model.Version, model.ArtifactKind, model.ArtifactReference,
            model.Digest, model.Description, model.SupportedAssetKinds, model.CpuMillis, model.MemoryMib,
            model.StorageGib, model.Ports, model.ParameterSchema, model.HealthDeclaration, model.ProtocolEventTypes);

    public static RegisterTeamLabDevicePackageModel ToInternal(this OpenUpdateTeamLabDevicePackageModel model) =>
        new(model.Name, model.DisplayName, model.Version, model.ArtifactKind, model.ArtifactReference,
            model.Digest, model.Description, model.SupportedAssetKinds, model.CpuMillis, model.MemoryMib,
            model.StorageGib, model.Ports, model.ParameterSchema, model.HealthDeclaration, model.ProtocolEventTypes);

    public static RegisterTeamLabConnectorModel ToInternal(this OpenRegisterTeamLabConnectorModel model) =>
        new(model.Name, model.DisplayName, model.Kind, model.ControlScopeId, model.SupportsSharedUse,
            model.Capacity, model.AttachmentReference, model.Description, model.ManagedNic);

    public static RegisterTeamLabConnectorModel ToInternal(this OpenUpdateTeamLabConnectorModel model) =>
        new(model.Name, model.DisplayName, model.Kind, model.ControlScopeId, model.SupportsSharedUse,
            model.Capacity, model.AttachmentReference, model.Description, model.ManagedNic);
}
