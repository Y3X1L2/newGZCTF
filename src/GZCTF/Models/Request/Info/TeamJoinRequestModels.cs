using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Request.Info;

public class TeamJoinRequestCreateModel
{
    [MaxLength(Limits.MaxUserDataLength)]
    public string? Message { get; set; }
}

public class TeamJoinRequestReviewModel
{
    public bool Accepted { get; set; }
}

public class TeamJoinRequestModel
{
    public int Id { get; set; }

    public int TeamId { get; set; }

    public string? TeamName { get; set; }

    public TeamUserInfoModel User { get; set; } = new();

    public string? Message { get; set; }

    public TeamJoinRequestStatus Status { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? ReviewedAtUtc { get; set; }

    internal static TeamJoinRequestModel FromRequest(TeamJoinRequest request) =>
        new()
        {
            Id = request.Id,
            TeamId = request.TeamId,
            TeamName = request.Team.Name,
            User = new TeamUserInfoModel
            {
                Id = request.User.Id,
                Bio = request.User.Bio,
                UserName = request.User.UserName,
                Avatar = request.User.AvatarUrl,
                Captain = false,
                RealName = request.User.RealName,
                StudentNumber = request.User.StdNumber
            },
            Message = request.Message,
            Status = request.Status,
            CreatedAtUtc = request.CreatedAtUtc,
            ReviewedAtUtc = request.ReviewedAtUtc
        };
}
