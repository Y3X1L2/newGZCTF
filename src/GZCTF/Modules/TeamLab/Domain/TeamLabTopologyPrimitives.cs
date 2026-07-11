namespace GZCTF.Modules.TeamLab.Domain;

public enum TeamLabAssetKind : byte
{
    Docker = 0,
    Vm = 1
}

public enum TeamLabHealthCheckKind : byte
{
    Tcp = 0,
    Http = 1
}
