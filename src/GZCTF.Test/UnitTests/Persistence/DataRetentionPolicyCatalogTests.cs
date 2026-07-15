using System.Linq;
using GZCTF.Infrastructure.Persistence.Governance;
using Microsoft.Extensions.Options;
using Xunit;

namespace GZCTF.Test.UnitTests.Persistence;

public sealed class DataRetentionPolicyCatalogTests
{
    [Fact]
    public void Catalog_RegistersEveryGovernedDataSetWithExplicitSemantics()
    {
        var catalog = new DataRetentionPolicyCatalog(Options.Create(new DataRetentionOptions()));

        string[] expected =
        [
            "participation", "submission", "training-progress", "theory-answer",
            "awdp-competition", "system-log", "operational-event", "teamlab-flow", "teamlab-flow-aggregate",
            "deployment-ticket", "api-operation", "teamlab-event", "governance-run",
            "worker-node-metric"
        ];
        Assert.Equal(expected.Order(), catalog.Policies.Select(policy => policy.Name).Order());
        Assert.All(catalog.Policies, policy =>
        {
            Assert.False(string.IsNullOrWhiteSpace(policy.OwnerModule));
            Assert.False(string.IsNullOrWhiteSpace(policy.ArchiveAction));
            Assert.False(string.IsNullOrWhiteSpace(policy.FailureMode));
        });
    }

    [Fact]
    public void Catalog_NeverAssignsAutomaticRetentionToCoreBusinessFacts()
    {
        var catalog = new DataRetentionPolicyCatalog(Options.Create(new DataRetentionOptions()));

        foreach (var name in new[]
                 {
                     "participation", "submission", "training-progress", "theory-answer", "awdp-competition"
                 })
        {
            var policy = catalog.GetRequired(name);
            Assert.Equal(DataLifecycleMode.OwnerManaged, policy.Mode);
            Assert.Null(policy.RawRetention);
            Assert.Null(policy.AggregateRetention);
        }
    }
}
