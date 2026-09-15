using GZCTF.TeamLab.Contracts.Execution;

namespace GZCTF.Modules.TeamLab.Contracts;

public sealed record TeamLabDeviceHealthModel(
    int AssetId,
    string Name,
    int Generation,
    TeamLabDeviceObservation? Observation,
    DateTimeOffset? NextProbeAt);
