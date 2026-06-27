using System.ComponentModel.DataAnnotations;

using GZCTF.Models.Request;

namespace GZCTF.Models.Request.Admin;

/// <summary>
/// Batch user creation (Admin)
/// </summary>
public class UserCreateModel
{
    /// <summary>
    /// Username
    /// </summary>
    [Required(ErrorMessageResourceName = nameof(Resources.Program.Model_UserNameRequired),
        ErrorMessageResourceType = typeof(Resources.Program))]
    [MinLength(Limits.MinUserNameLength, ErrorMessageResourceName = nameof(Resources.Program.Model_UserNameTooShort),
        ErrorMessageResourceType = typeof(Resources.Program))]
    [MaxLength(Limits.MaxUserNameLength, ErrorMessageResourceName = nameof(Resources.Program.Model_UserNameTooLong),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Password
    /// </summary>
    [Required(ErrorMessageResourceName = nameof(Resources.Program.Model_PasswordRequired),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Email
    /// </summary>
    [Required(ErrorMessageResourceName = nameof(Resources.Program.Model_EmailRequired),
        ErrorMessageResourceType = typeof(Resources.Program))]
    [EmailAddress(ErrorMessageResourceName = nameof(Resources.Program.Model_EmailMalformed),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Real name
    /// </summary>
    [MaxLength(Limits.MaxUserDataLength, ErrorMessageResourceName = nameof(Resources.Program.Model_RealNameTooLong),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string? RealName { get; set; }

    /// <summary>
    /// Student number
    /// </summary>
    [MaxLength(Limits.MaxStdNumberLength, ErrorMessageResourceName = nameof(Resources.Program.Model_StdNumberTooLong),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string? StdNumber { get; set; }

    /// <summary>
    /// Contact phone number
    /// </summary>
    [PhoneNumber(ErrorMessageResourceName = nameof(Resources.Program.Model_MalformedPhoneNumber),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string? Phone { get; set; }

    /// <summary>
    /// Team the user joins
    /// </summary>
    [MaxLength(Limits.MaxTeamNameLength, ErrorMessageResourceName = nameof(Resources.Program.Model_TeamNameTooLong),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string? TeamName { get; set; }

    /// <summary>
    /// Role assigned to the user
    /// </summary>
    public Role? AssignedRole { get; set; }

    /// <summary>
    /// Student groups to join after creation
    /// </summary>
    public List<int>? StudentGroupIds { get; set; }

    internal UserInfo ToUserInfo() =>
        new()
        {
            Email = Email,
            UserName = UserName,
            RealName = RealName ?? "",
            StdNumber = StdNumber ?? "",
            PhoneNumber = Phone,
            Role = AssignedRole ?? Utils.Role.Student,
            EmailConfirmed = true
        };
}
