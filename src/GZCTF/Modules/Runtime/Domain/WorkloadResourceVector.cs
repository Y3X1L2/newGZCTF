namespace GZCTF.Modules.Runtime.Domain;

public readonly record struct WorkloadResourceVector(
    long CpuUnits,
    long MemoryMiB,
    long StorageMiB,
    int DockerSlots,
    int VmSlots)
{
    public static WorkloadResourceVector Zero => new(0, 0, 0, 0, 0);

    public bool IsNonNegative =>
        CpuUnits >= 0 &&
        MemoryMiB >= 0 &&
        StorageMiB >= 0 &&
        DockerSlots >= 0 &&
        VmSlots >= 0;

    public bool CanFit(WorkloadResourceVector required) =>
        required.IsNonNegative &&
        CpuUnits >= required.CpuUnits &&
        MemoryMiB >= required.MemoryMiB &&
        StorageMiB >= required.StorageMiB &&
        DockerSlots >= required.DockerSlots &&
        VmSlots >= required.VmSlots;

    public static WorkloadResourceVector operator +(
        WorkloadResourceVector left,
        WorkloadResourceVector right) =>
        new(
            left.CpuUnits + right.CpuUnits,
            left.MemoryMiB + right.MemoryMiB,
            left.StorageMiB + right.StorageMiB,
            left.DockerSlots + right.DockerSlots,
            left.VmSlots + right.VmSlots);

    public static WorkloadResourceVector operator -(
        WorkloadResourceVector left,
        WorkloadResourceVector right) =>
        new(
            left.CpuUnits - right.CpuUnits,
            left.MemoryMiB - right.MemoryMiB,
            left.StorageMiB - right.StorageMiB,
            left.DockerSlots - right.DockerSlots,
            left.VmSlots - right.VmSlots);
}
