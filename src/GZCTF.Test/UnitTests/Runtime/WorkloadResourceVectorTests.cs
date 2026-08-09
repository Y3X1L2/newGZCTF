using GZCTF.Modules.Runtime.Domain;
using Xunit;

namespace GZCTF.Test.UnitTests.Runtime;

public sealed class WorkloadResourceVectorTests
{
    [Fact]
    public void AvailableVector_RejectsARequestThatExceedsMemory()
    {
        var total = new WorkloadResourceVector(16_000, 32_768, 500_000, 20, 4);
        var used = new WorkloadResourceVector(8_000, 24_576, 100_000, 8, 2);
        var request = new WorkloadResourceVector(2_000, 10_240, 50_000, 1, 0);

        Assert.False((total - used).CanFit(request));
    }

    [Fact]
    public void Addition_SumsEveryResourceDimension()
    {
        var left = new WorkloadResourceVector(2_000, 2_048, 30_000, 2, 1);
        var right = new WorkloadResourceVector(1_000, 1_024, 20_000, 3, 2);

        var result = left + right;

        Assert.Equal(new WorkloadResourceVector(3_000, 3_072, 50_000, 5, 3), result);
        Assert.Equal(new WorkloadResourceVector(2_000, 2_048, 30_000, 2, 1), left);
    }

    [Fact]
    public void CanFit_RejectsNegativeResourceRequests()
    {
        var available = new WorkloadResourceVector(1_000, 1_024, 20_000, 1, 0);

        Assert.False(available.CanFit(new WorkloadResourceVector(-1, 0, 0, 0, 0)));
    }

    [Fact]
    public void Subtraction_RepresentsOvercommitWithoutThrowing()
    {
        var available = new WorkloadResourceVector(1_000, 1_024, 20_000, 1, 0);
        var used = new WorkloadResourceVector(1_001, 1_024, 20_000, 1, 0);

        var remaining = available - used;

        Assert.Equal(-1, remaining.CpuUnits);
        Assert.False(remaining.CanFit(WorkloadResourceVector.Zero));
    }
}
