namespace GZCTF.Modules.TeamLab.Domain;

public enum TeamLabInfrastructureKind : byte
{
    ManagedSwitch = 0,
    ManagedRouter = 1
}

public enum TeamLabConnectionDirection : byte
{
    FromTo = 0,
    Bidirectional = 1
}

public enum TeamLabEndpointObservationMode : byte
{
    Disabled = 0,
    Optional = 1,
    Required = 2
}
