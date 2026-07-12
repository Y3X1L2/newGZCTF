using GZCTF.Models.Data;

namespace GZCTF.Modules.Theory.Application;

public interface ITheoryQuestionCatalog
{
    Task<TheoryQuestionBankItem[]> SearchAsync(
        string? keyword,
        IReadOnlyCollection<string> tags,
        int skip,
        int count,
        CancellationToken cancellationToken);

    Task<TheoryQuestionBankItem?> FindForUpdateAsync(int id, CancellationToken cancellationToken);

    Task SetTagsAsync(
        TheoryQuestionBankItem question,
        IEnumerable<string> tags,
        CancellationToken cancellationToken);
}
