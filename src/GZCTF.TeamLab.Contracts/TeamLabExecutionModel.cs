namespace GZCTF.TeamLab.Contracts;

/// <summary>
/// Execution model selected for TeamLab runtime deployments. V2 is the platform default;
/// V1 is retained only as an explicit migration mode and is never chosen automatically.
/// </summary>
public enum TeamLabExecutionModel
{
    V1 = 0,
    V2 = 1
}
