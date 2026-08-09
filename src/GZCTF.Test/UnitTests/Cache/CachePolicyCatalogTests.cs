using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using GZCTF.Infrastructure.Cache;
using GZCTF.Models.Data;
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

    [Fact]
    public void PostListSerializer_PreservesIpAddress()
    {
        var expected = new DataWithModifiedTime<Post[]>(
        [
            new Post
            {
                Id = "phase009",
                Title = "Phase 9",
                Summary = "Validation",
                Content = "Ready",
                Author = new UserInfo
                {
                    UserName = "admin",
                    IP = IPAddress.Parse("10.0.7.118")
                }
            }
        ], DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        var writer = new ArrayBufferWriter<byte>();
        var serializer = new PostListHybridCacheSerializer();

        serializer.Serialize(expected, writer);
        var restored = serializer.Deserialize(new ReadOnlySequence<byte>(writer.WrittenMemory));

        Assert.Equal(expected.Data[0].Author!.IP, restored.Data[0].Author!.IP);
        Assert.Equal(expected.LastModifiedTimeUtc, restored.LastModifiedTimeUtc);
    }
}
