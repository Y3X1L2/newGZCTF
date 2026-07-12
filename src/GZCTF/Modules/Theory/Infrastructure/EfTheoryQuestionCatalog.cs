using GZCTF.Models.Data;
using GZCTF.Modules.Theory.Application;
using GZCTF.Modules.Theory.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Theory.Infrastructure;

public sealed class EfTheoryQuestionCatalog(AppDbContext context) : ITheoryQuestionCatalog
{
    public async Task<TheoryQuestionBankItem[]> SearchAsync(
        string? keyword,
        IReadOnlyCollection<string> tags,
        int skip,
        int count,
        CancellationToken cancellationToken)
    {
        var query = context.TheoryQuestionBankItems.AsNoTracking()
            .Include(item => item.TagBindings).ThenInclude(binding => binding.Tag)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var pattern = $"%{keyword.Trim()}%";
            query = query.Where(item =>
                EF.Functions.ILike(item.Title, pattern) ||
                EF.Functions.ILike(item.BankName, pattern));
        }

        var normalizedTags = TheoryTagNormalizer.NormalizeMany(tags)
            .Select(tag => tag.NormalizedName)
            .ToArray();
        if (normalizedTags.Length > 0)
            query = query.Where(item => item.TagBindings.Any(binding =>
                normalizedTags.Contains(binding.Tag.NormalizedName)));

        query = query.OrderByDescending(item => item.UpdatedAt).ThenByDescending(item => item.Id);
        if (count > 0)
            query = query.Skip(Math.Max(0, skip)).Take(count);

        return await query.ToArrayAsync(cancellationToken);
    }

    public Task<TheoryQuestionBankItem?> FindForUpdateAsync(int id, CancellationToken cancellationToken) =>
        context.TheoryQuestionBankItems
            .Include(item => item.TagBindings).ThenInclude(binding => binding.Tag)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    public async Task SetTagsAsync(
        TheoryQuestionBankItem question,
        IEnumerable<string> tags,
        CancellationToken cancellationToken)
    {
        var normalized = TheoryTagNormalizer.NormalizeMany(tags);
        var normalizedNames = normalized.Select(tag => tag.NormalizedName).ToArray();
        var existingTags = await context.TheoryQuestionTags
            .Where(tag => normalizedNames.Contains(tag.NormalizedName))
            .ToDictionaryAsync(tag => tag.NormalizedName, StringComparer.Ordinal, cancellationToken);

        var desiredTags = new List<TheoryQuestionTag>(normalized.Count);
        foreach (var value in normalized)
        {
            if (!existingTags.TryGetValue(value.NormalizedName, out var tag))
            {
                tag = new TheoryQuestionTag
                {
                    DisplayName = value.DisplayName,
                    NormalizedName = value.NormalizedName
                };
                context.TheoryQuestionTags.Add(tag);
                existingTags.Add(value.NormalizedName, tag);
            }

            desiredTags.Add(tag);
        }

        if (question.TagBindings.Count > 0)
            context.TheoryQuestionTagBindings.RemoveRange(question.TagBindings);
        question.TagBindings = desiredTags.Select(tag => new TheoryQuestionTagBinding
        {
            QuestionId = question.Id,
            Tag = tag
        }).ToList();
    }
}
