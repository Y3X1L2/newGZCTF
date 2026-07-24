using GZCTF.Modules.TeamLab.Application.Validation;
using GZCTF.Modules.TeamLab.Contracts;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabTopologyValidator
{
    public const int MaxNetworks = 32;
    public const int MaxAssets = 128;
    public const int MaxInterfacesPerAsset = 8;

    private readonly TeamLabTopologyStructureValidator _structure = new();
    private readonly TeamLabDependencyGraphValidator _dependencies = new();

    public TeamLabValidationResultModel Validate(TeamLabTopologyDefinitionModel definition, int schemaVersion = 2)
    {
        var issues = new List<TeamLabValidationIssueModel>();
        if (schemaVersion is not 1 and not 2)
        {
            issues.Add(new TeamLabValidationIssueModel(
                "topology_schema_unsupported",
                "schemaVersion",
                $"Topology schema version {schemaVersion} is not supported."));
            return new TeamLabValidationResultModel(false, issues);
        }

        _structure.Validate(definition, schemaVersion, issues);
        if (schemaVersion == 2)
            _dependencies.Validate(definition, issues);
        return new TeamLabValidationResultModel(issues.Count == 0, issues);
    }
}
