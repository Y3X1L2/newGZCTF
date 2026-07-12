namespace GZCTF.Modules.Theory.Domain;

public sealed class TheoryQuestionTag
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<TheoryQuestionTagBinding> Questions { get; set; } = [];
}

public sealed class TheoryQuestionTagBinding
{
    public int QuestionId { get; set; }
    public int TagId { get; set; }
    public TheoryQuestionTag Tag { get; set; } = null!;
}
