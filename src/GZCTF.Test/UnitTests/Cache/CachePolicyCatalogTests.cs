using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using GZCTF.Infrastructure.Cache;
using GZCTF.Models.Request.Game;
using GZCTF.Utils;
using Xunit;

namespace GZCTF.Test.UnitTests.Cache;

public sealed class CachePolicyCatalogTests
{
    [Fact]
    public void Catalog_HasCanonicalUniquePolicies()
    {
        var catalog = new CachePolicyCatalog();

        Assert.NotEmpty(catalog.All);
        Assert.Equal(catalog.All.Count,
            catalog.All.Select(policy => policy.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(catalog.All, policy =>
        {
            policy.Validate();
            Assert.True(policy.DistributedTtl >= policy.LocalTtl);
            Assert.True(policy.SizeLimit > 0);
        });
    }

    [Fact]
    public void HighConsistencyPolicies_UseProjectionRevision()
    {
        Assert.Equal(CacheConsistencyMode.ProjectionRevision, CachePolicyCatalog.Scoreboard.ConsistencyMode);
        Assert.Equal(CacheConsistencyMode.ProjectionRevision, CachePolicyCatalog.TrainingStatistics.ConsistencyMode);
        Assert.Equal(CacheConsistencyMode.ProjectionRevision, CachePolicyCatalog.TheoryStatistics.ConsistencyMode);
    }

    [Fact]
    public void ScoreboardSerializer_PreservesInternalProjectionState()
    {
        var model = new ScoreboardModel
        {
            TimeLines = new Dictionary<int, IEnumerable<TopTimeLine>> { [1] = [] },
            Items = new Dictionary<int, ScoreboardItem> { [2] = new() { Id = 2, Name = "team" } },
            Divisions = new Dictionary<int, DivisionItem> { [1] = new() { Id = 1, Name = "default" } },
            Challenges = new Dictionary<ChallengeCategory, IEnumerable<ChallengeInfo>>()
        };
        var writer = new ArrayBufferWriter<byte>();
        var serializer = new ScoreboardHybridCacheSerializer();

        serializer.Serialize(model, writer);
        var restored = serializer.Deserialize(new ReadOnlySequence<byte>(writer.WrittenMemory));

        Assert.Single(restored.TimeLines);
        Assert.Single(restored.Items);
        Assert.Single(restored.Divisions);
        Assert.NotNull(restored.ChallengeMap);
    }
}
