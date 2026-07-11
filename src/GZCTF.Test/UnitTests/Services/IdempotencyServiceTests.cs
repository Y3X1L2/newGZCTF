using System;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Services;

public sealed class IdempotencyServiceTests
{
    [Fact]
    public async Task BeginAsync_ReusesSameRequestAndRejectsChangedPayload()
    {
        ApiOperation? persisted = null;
        var store = new Mock<IApiOperationStore>();
        store.Setup(item => item.FindIdempotentAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => persisted);
        store.Setup(item => item.AddAsync(It.IsAny<ApiOperation>(), It.IsAny<CancellationToken>()))
            .Callback<ApiOperation, CancellationToken>((operation, _) => persisted = operation)
            .Returns(Task.CompletedTask);

        var service = new IdempotencyService(store.Object);
        var tokenId = Guid.CreateVersion7();

        var first = await service.BeginAsync(tokenId, "images.register", "key-001", "hash-a", CancellationToken.None);
        var retry = await service.BeginAsync(tokenId, "images.register", "key-001", "hash-a", CancellationToken.None);

        Assert.Equal(first.Operation.Id, retry.Operation.Id);
        Assert.False(first.Reused);
        Assert.True(retry.Reused);

        var conflict = await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            service.BeginAsync(tokenId, "images.register", "key-001", "hash-b", CancellationToken.None));
        Assert.Equal("idempotency_conflict", conflict.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task BeginAsync_RejectsMissingIdempotencyKey(string key)
    {
        var service = new IdempotencyService(Mock.Of<IApiOperationStore>());

        var exception = await Assert.ThrowsAsync<IdempotencyValidationException>(() =>
            service.BeginAsync(Guid.CreateVersion7(), "images.register", key, "hash-a", CancellationToken.None));

        Assert.Equal("idempotency_key_required", exception.Code);
    }
}
