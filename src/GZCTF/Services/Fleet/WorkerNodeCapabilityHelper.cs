using GZCTF.Models.Data;

namespace GZCTF.Services.Fleet;

public static class WorkerNodeCapabilityHelper
{
    public static NodeCapability FromTeamLabReport(bool docker, bool kvm, bool kvmDevice, bool cpuVirtualization)
    {
        var capabilities = NodeCapability.None;
        if (docker)
            capabilities |= NodeCapability.Docker;
        if (kvm && kvmDevice && cpuVirtualization)
            capabilities |= NodeCapability.Kvm;
        return capabilities;
    }

    public static NodeCapability FromTeamLabReport(TeamLabToolCapabilityReport report) =>
        FromTeamLabReport(report.Docker, report.Kvm, report.KvmDevice, report.CpuVirtualization);

}
