using System.Buffers;
using GZCTF.Models.Request.Game;
using MemoryPack;
using Microsoft.Extensions.Caching.Hybrid;

namespace GZCTF.Infrastructure.Cache;

public sealed class ScoreboardHybridCacheSerializer : IHybridCacheSerializer<ScoreboardModel>
{
    public ScoreboardModel Deserialize(ReadOnlySequence<byte> source) =>
        MemoryPackSerializer.Deserialize<ScoreboardModel>(source.ToArray())
        ?? throw new InvalidOperationException("Cached scoreboard payload is empty.");

    public void Serialize(ScoreboardModel value, IBufferWriter<byte> target) =>
        MemoryPackSerializer.Serialize(target, value);
}
