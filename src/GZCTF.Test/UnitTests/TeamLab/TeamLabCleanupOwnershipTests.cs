using GZCTF.Agent.Services;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

/// <summary>
/// Cleanup removes bridges, the router namespace, veth pairs and dnsmasq only when it can prove it
/// owns them. Those names are reused across generations of one runtime, so the decision doubles as
/// the generation fencing token and stays deliberately conservative: unproven ownership never
/// deletes. The leak that motivated extracting this is fixed at its source instead — apply now
/// claims the marker before its first mutating command, so a half-applied generation is provably
/// owned rather than something this decision has to infer.
/// </summary>
public class TeamLabCleanupOwnershipTests
{
    [Fact]
    public void MatchingGeneration_IsOwned()
    {
        var ownership = TeamLabNetworkService.ResolveCleanupOwnership(
            activeGeneration: 4, requestGeneration: 4, desiredStateExists: true, dryRun: false);

        Assert.Equal(TeamLabCleanupOwnership.OwnsSharedResources, ownership);
    }

    [Theory]
    [InlineData(5, 4)] // late cleanup for an older generation while a newer one runs
    [InlineData(4, 5)] // marker older than the request
    public void DifferentGeneration_LeavesSharedResourcesAlone(int active, int requested)
    {
        var ownership = TeamLabNetworkService.ResolveCleanupOwnership(
            activeGeneration: active, requestGeneration: requested, desiredStateExists: true,
            dryRun: false);

        Assert.Equal(TeamLabCleanupOwnership.SharedResourcesNotOwned, ownership);
    }

    [Fact]
    public void NoMarkerAndNoDesiredState_LeavesSharedResourcesAlone()
    {
        // Ownership is unproven, and shared names may belong to a concurrent generation.
        var ownership = TeamLabNetworkService.ResolveCleanupOwnership(
            activeGeneration: null, requestGeneration: 3, desiredStateExists: false, dryRun: false);

        Assert.Equal(TeamLabCleanupOwnership.SharedResourcesNotOwned, ownership);
    }

    [Fact]
    public void DesiredStateWithoutMarker_FailsClosed()
    {
        // Apply writes the marker before the desired state, so this combination means the marker
        // was lost rather than never written. Refuse instead of guessing.
        var ownership = TeamLabNetworkService.ResolveCleanupOwnership(
            activeGeneration: null, requestGeneration: 2, desiredStateExists: true, dryRun: false);

        Assert.Equal(TeamLabCleanupOwnership.Refuse, ownership);
    }

    [Fact]
    public void DryRunNeverRefuses_SoOperatorsCanStillSeeThePlan()
    {
        var ownership = TeamLabNetworkService.ResolveCleanupOwnership(
            activeGeneration: null, requestGeneration: 2, desiredStateExists: true, dryRun: true);

        Assert.Equal(TeamLabCleanupOwnership.SharedResourcesNotOwned, ownership);
    }
}
