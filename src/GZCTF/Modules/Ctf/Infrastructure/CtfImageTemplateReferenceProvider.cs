using GZCTF.Models;
using GZCTF.Modules.Content.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Ctf.Infrastructure;

public sealed class CtfImageTemplateReferenceProvider(AppDbContext context)
    : IImageTemplateReferenceProvider
{
    public string Module => "CTF";

    public async Task<IReadOnlyList<ImageTemplateReference>> GetReferencesAsync(
        int imageTemplateId,
        CancellationToken cancellationToken)
    {
        var items = await context.GameChallenges.AsNoTracking()
            .Where(challenge => challenge.ImageTemplateId == imageTemplateId)
            .Select(challenge => new { challenge.Id, challenge.Title })
            .ToArrayAsync(cancellationToken);
        return items.Select(item => new ImageTemplateReference(
            Module, "challenge", item.Id.ToString(), item.Title)).ToArray();
    }
}
