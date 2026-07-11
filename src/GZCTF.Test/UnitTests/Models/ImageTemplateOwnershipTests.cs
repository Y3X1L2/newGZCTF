using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Identity.Application;
using GZCTF.Utils;
using Xunit;

namespace GZCTF.Test.UnitTests.Models;

public sealed class ImageTemplateOwnershipTests
{
    [Fact]
    public async Task DeleteTemplate_RejectsAnyActiveBusinessReference()
    {
        IImageTemplateReferenceProvider[] providers =
        [
            new StubProvider("Training", [
                new ImageTemplateReference("Training", "course", "42", "Course 42")
            ]),
            new StubProvider("CTF", [])
        ];
        var service = new ImageTemplateReferenceService(providers);

        var result = await service.CanDeleteAsync(7, CancellationToken.None);

        Assert.False(result.Allowed);
        var reference = Assert.Single(result.References);
        Assert.Equal("Training", reference.Module);
    }

    [Fact]
    public async Task DeleteTemplate_AllowsTemplateWithoutBusinessReferences()
    {
        var service = new ImageTemplateReferenceService(
            [new StubProvider("Training", []), new StubProvider("CTF", [])]);

        var result = await service.CanDeleteAsync(7, CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.Empty(result.References);
    }

    [Fact]
    public async Task ReferenceProviders_AreQueriedSequentiallyForScopedPersistence()
    {
        var guard = new ProviderConcurrencyGuard();
        var service = new ImageTemplateReferenceService(
            [new GuardedProvider("CTF", guard), new GuardedProvider("Training", guard)]);

        var result = await service.CanDeleteAsync(7, CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.Equal(1, guard.MaximumConcurrency);
    }

    [Theory]
    [InlineData(Role.Teacher, true, ImageTemplateDeleteStatus.Deleted)]
    [InlineData(Role.Teacher, false, ImageTemplateDeleteStatus.Forbidden)]
    [InlineData(Role.Admin, false, ImageTemplateDeleteStatus.Deleted)]
    public async Task DeleteTemplate_EnforcesOwnerAndSystemTemplateRules(
        Role role,
        bool actorOwnsTemplate,
        ImageTemplateDeleteStatus expected)
    {
        var actorId = Guid.NewGuid();
        var catalog = new StubCatalog(actorOwnsTemplate ? actorId : null);
        var service = new ImageTemplateDeletionService(
            catalog,
            new ImageTemplateReferenceService([new StubProvider("CTF", [])]));

        var result = await service.DeleteAsync(
            7,
            new ActorContext(actorId, role),
            CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Equal(expected == ImageTemplateDeleteStatus.Deleted, catalog.Deleted);
    }

    private sealed class StubProvider(
        string module,
        IReadOnlyList<ImageTemplateReference> references) : IImageTemplateReferenceProvider
    {
        public string Module => module;

        public Task<IReadOnlyList<ImageTemplateReference>> GetReferencesAsync(
            int imageTemplateId,
            CancellationToken cancellationToken) =>
            Task.FromResult(references);
    }

    private sealed class ProviderConcurrencyGuard
    {
        private int _current;
        private int _maximum;

        public int MaximumConcurrency => Volatile.Read(ref _maximum);

        public async Task EnterAsync(CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _current);
            var maximum = Volatile.Read(ref _maximum);
            while (current > maximum)
            {
                var observed = Interlocked.CompareExchange(ref _maximum, current, maximum);
                if (observed == maximum)
                    break;
                maximum = observed;
            }

            try
            {
                await Task.Delay(20, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }
    }

    private sealed class GuardedProvider(
        string module,
        ProviderConcurrencyGuard guard) : IImageTemplateReferenceProvider
    {
        public string Module => module;

        public async Task<IReadOnlyList<ImageTemplateReference>> GetReferencesAsync(
            int imageTemplateId,
            CancellationToken cancellationToken)
        {
            await guard.EnterAsync(cancellationToken);
            return [];
        }
    }

    private sealed class StubCatalog(Guid? createdById) : IImageTemplateCatalog
    {
        public bool Deleted { get; private set; }

        public Task<ImageTemplateDescriptor?> FindAsync(
            int id,
            CancellationToken cancellationToken) =>
            Task.FromResult<ImageTemplateDescriptor?>(new(id, createdById, "template"));

        public Task<ImageTemplateDetails?> FindDetailsAsync(
            int id,
            CancellationToken cancellationToken) =>
            Task.FromResult<ImageTemplateDetails?>(null);

        public Task<ImageTemplateDeleteDecision> MarkDeletingAsync(
            int id,
            Func<CancellationToken, Task<ImageTemplateDeleteDecision>> checkReferences,
            CancellationToken cancellationToken) =>
            checkReferences(cancellationToken);

        public Task CompleteDeletionAsync(int id, CancellationToken cancellationToken)
        {
            Deleted = true;
            return Task.CompletedTask;
        }
    }
}
