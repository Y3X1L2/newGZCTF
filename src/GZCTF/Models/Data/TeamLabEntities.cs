using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

public enum TeamLabRuntimeStatus : byte
{
    Pending = 0,
    Planning = 1,
    Scheduled = 2,
    Deploying = 3,
    Probing = 4,
    Running = 5,
    Failed = 6,
    CleanupPending = 7,
    Stopped = 8,
    Destroying = 9,
    Destroyed = 10
}

public enum TeamLabResourceKind : byte
{
    Docker = 0,
    Vm = 1,
    RouterNamespace = 2,
    DhcpDnsService = 3,
    WireGuard = 4,
    PublicUdpMapping = 5
}

public enum TeamLabEventLevel : byte
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}
