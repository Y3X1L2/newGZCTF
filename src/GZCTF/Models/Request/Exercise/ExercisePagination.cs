using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Request.Exercise;

public sealed class ExercisePagination
{
    [Range(1, 1000000)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}

public sealed class ExerciseCatalogPageModel
{
    public ExerciseInfoModel[] Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int EnabledCount { get; set; }
    public int ContainerCount { get; set; }
    public string[] Tags { get; set; } = [];
}
