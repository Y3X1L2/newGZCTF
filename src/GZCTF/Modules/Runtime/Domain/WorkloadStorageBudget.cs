namespace GZCTF.Modules.Runtime.Domain;

public static class WorkloadStorageBudget
{
    // Aggregate requests do not identify per-filesystem storage, so mixed placement uses the smaller budget.
    public static long For(WorkloadResourceVector requested, long dockerAvailableMiB, long vmAvailableMiB) =>
        requested.DockerSlots > 0 && requested.VmSlots == 0
            ? dockerAvailableMiB
            : requested.DockerSlots > 0 && requested.VmSlots > 0
                ? Math.Min(dockerAvailableMiB, vmAvailableMiB)
                : vmAvailableMiB;
}
