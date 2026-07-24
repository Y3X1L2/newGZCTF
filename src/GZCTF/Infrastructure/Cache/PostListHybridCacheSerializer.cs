using System.Buffers;
using MemoryPack;
using Microsoft.Extensions.Caching.Hybrid;

namespace GZCTF.Infrastructure.Cache;

public sealed class PostListHybridCacheSerializer :
    IHybridCacheSerializer<DataWithModifiedTime<Post[]>>
{
    public DataWithModifiedTime<Post[]> Deserialize(ReadOnlySequence<byte> source) =>
        MemoryPackSerializer.Deserialize<DataWithModifiedTime<Post[]>>(source.ToArray())
        ?? throw new InvalidOperationException("Cached post list payload is empty.");

    public void Serialize(DataWithModifiedTime<Post[]> value, IBufferWriter<byte> target) =>
        MemoryPackSerializer.Serialize(target, value);
}
