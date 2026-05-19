using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

[PrimaryKey(nameof(StageId), nameof(RequiredStageId))]
public class StageDependency
{
    public int StageId { get; set; }
    public int RequiredStageId { get; set; }

    [ForeignKey(nameof(StageId))]
    public Stage? Stage { get; set; }

    [ForeignKey(nameof(RequiredStageId))]
    public Stage? RequiredStage { get; set; }
}
