using System.Net.Mime;
using System.Globalization;
using GZCTF.Middlewares;
using GZCTF.Models;
using GZCTF.Models.Internal;
using GZCTF.Models.Request.Account;
using GZCTF.Models.Request.Info;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using GZCTF.Services.Config;
using GZCTF.Services.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace GZCTF.Controllers;

/// <summary>
/// User account related APIs
/// </summary>
[ApiController]
[Route("api/[controller]/[action]")]
[Produces(MediaTypeNames.Application.Json)]
public class AccountController(
    IMailSender mailSender,
    IBlobRepository blobService,
    IHostEnvironment environment,
    ICaptchaService captcha,
    IConfigService configService,
    PortalSsoService portalSsoService,
    IOptionsSnapshot<AccountPolicy> accountPolicy,
    IOptionsSnapshot<GlobalConfig> globalConfig,
    IOptionsSnapshot<PortalSsoConfig> portalSsoConfig,
    UserProfileQueryService userProfiles,
    UserManager<UserInfo> userManager,
    SignInManager<UserInfo> signInManager,
    ILogger<AccountController> logger,
    IStringLocalizer<Program> localizer) : ControllerBase
{
    /// <summary>
    /// Get public account capabilities used by authentication pages.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AccountCapabilitiesModel), StatusCodes.Status200OK)]
    public IActionResult Capabilities() =>
        Ok(AccountCapabilitiesModel.FromConfig(accountPolicy.Value, portalSsoConfig.Value));

    /// <summary>
    /// Get the lightweight identity and activity summary used by the account drawer.
    /// </summary>
    [HttpGet]
    [RequireUser]
    [ProducesResponseType(typeof(AccountSummaryModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Summary(CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized(new RequestResponse(localizer[nameof(Resources.Program.Auth_LoginRequired)],
                StatusCodes.Status401Unauthorized));

        var summary = await userProfiles.GetAccountSummaryAsync(user.Id, token);
        return summary is null
            ? NotFound(new RequestResponse(localizer[nameof(Resources.Program.Account_UserNotExist)],
                StatusCodes.Status404NotFound))
            : Ok(summary);
    }

    /// <summary>
    /// Login through the unified portal IAM service.
    /// </summary>
    /// <param name="portalToken">Token passed by the portal dashboard.</param>
    /// <param name="returnUrl">Local URL to redirect to after login.</param>
    /// <param name="token"></param>
    /// <response code="302">SSO login successful</response>
    /// <response code="400">Invalid request</response>
    /// <response code="401">Portal token rejected</response>
    /// <response code="403">Portal account does not have access</response>
    [HttpGet("/api/account/portal-sso")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> PortalSso(
        [FromQuery(Name = "portal_token")] string? portalToken,
        [FromQuery] string? returnUrl = "/",
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(portalToken))
            return BadRequest(new RequestResponse("Missing portal token."));

        var redirectUrl = NormalizePortalReturnUrl(returnUrl);
        if (redirectUrl is null)
            return BadRequest(new RequestResponse("Invalid return URL."));

        var profileResult = await portalSsoService.GetProfileAsync(portalToken, token);
        if (!profileResult.Succeeded || profileResult.Profile?.User is null)
            return RequestResponse.Result(profileResult.Error ?? "Portal SSO login failed.", profileResult.StatusCode);

        var portalUser = profileResult.Profile.User;
        var loginProvider = NormalizeLoginProvider(portalSsoConfig.Value.LoginProvider);
        var providerKey = portalUser.Id.ToString(CultureInfo.InvariantCulture);

        var user = await userManager.FindByLoginAsync(loginProvider, providerKey);
        if (user is null)
        {
            var (createdUser, errors) = await FindOrCreatePortalUser(portalUser, loginProvider, providerKey);
            if (createdUser is null)
                return HandleIdentityError(errors ?? []);

            user = createdUser;
        }

        if (user.Role == Role.Banned)
            return Unauthorized(new RequestResponse(localizer[nameof(Resources.Program.Account_UserDisabled)],
                StatusCodes.Status401Unauthorized));

        if (portalSsoConfig.Value.UpdateUserProfileOnLogin)
            UpdatePortalUserInfo(user, portalUser);

        user.EmailConfirmed = true;
        user.LastSignedInUtc = DateTimeOffset.UtcNow;
        user.UpdateByHttpContext(HttpContext);

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return HandleIdentityError(updateResult.Errors);

        await signInManager.SignOutAsync();
        await signInManager.SignInAsync(user, true);

        logger.LogInformation("Portal SSO user {PortalUserId} signed in as local user {UserId}.",
            portalUser.Id, user.Id);

        return LocalRedirect(redirectUrl);
    }

    /// <summary>
    /// User registration
    /// </summary>
    /// <remarks>
    /// Use this API to register a new user. In development environment, no verification. Email URL: /verify
    /// </remarks>
    /// <param name="model"></param>
    /// <param name="token"></param>
    /// <response code="200">Registration successful</response>
    /// <response code="400">Validation failed or user already exists</response>
    [HttpPost]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Register))]
    [ProducesResponseType(typeof(RequestResponse<RegisterStatus>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterModel model, CancellationToken token = default)
    {
        if (!accountPolicy.Value.AllowRegister)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_RegisterNotEnabled)]));

        if (accountPolicy.Value.UseCaptcha && !await captcha.VerifyAsync(model, HttpContext, token))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_TokenValidationFailed)]));

        if (!VerifyEmailDomain(model.Email))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_AvailableEmailDomain),
                accountPolicy.Value.EmailDomainList]));

        var password = configService.DecryptApiData(model.Password);
        if (string.IsNullOrWhiteSpace(password))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Model_PasswordRequired)]));

        var user = new UserInfo { UserName = model.UserName, Email = model.Email, Role = Role.Student };

        user.UpdateByHttpContext(HttpContext);

        var result = await userManager.CreateAsync(user, password);

        if (!result.Succeeded)
        {
            var current = await userManager.FindByEmailAsync(model.Email);

            if (current is null)
                return HandleIdentityError(result.Errors);

            if (await userManager.IsEmailConfirmedAsync(current))
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_UserExisting)]));

            user = current;
        }

        if (accountPolicy.Value.ActiveOnRegister)
        {
            user.EmailConfirmed = true;
            await userManager.UpdateAsync(user);
            await signInManager.SignInAsync(user, true);

            logger.Log(StaticLocalizer[nameof(Resources.Program.Account_UserRegisteredLog)], user,
                TaskStatus.Success);
            return Ok(new RequestResponse<RegisterStatus>(localizer[nameof(Resources.Program.Account_UserRegistered)],
                RegisterStatus.LoggedIn,
                StatusCodes.Status200OK));
        }

        if (!accountPolicy.Value.EmailConfirmationRequired)
        {
            logger.Log(StaticLocalizer[nameof(Resources.Program.Account_UserRegisteredWaitingApprovalLog)],
                user, TaskStatus.Success);
            return Ok(new RequestResponse<RegisterStatus>(
                localizer[nameof(Resources.Program.Account_UserRegisteredWaitingApproval)],
                RegisterStatus.AdminConfirmationRequired, StatusCodes.Status200OK));
        }

        logger.Log(StaticLocalizer[nameof(Resources.Program.Account_SendEmailVerification)], user,
            TaskStatus.Pending);

        var rToken = Codec.Base64.Encode(await userManager.GenerateEmailConfirmationTokenAsync(user));
        var link = GetEmailLink("verify", rToken, model.Email);

        if (environment.IsDevelopment())
        {
            logger.Log(link, user, TaskStatus.Pending, LogLevel.Debug);
        }
        else
        {
            if (!mailSender.SendConfirmEmailUrl(user.UserName, user.Email, link, localizer, globalConfig))
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_EmailSendFailed)]));
        }

        return Ok(new RequestResponse<RegisterStatus>(
            localizer[nameof(Resources.Program.Account_UserRegisteredWaitingEmailVerification)],
            RegisterStatus.EmailConfirmationRequired, StatusCodes.Status200OK));
    }

    private bool VerifyEmailDomain(string email)
    {
        var mailDomain = email.Split('@')[1];

        return string.IsNullOrWhiteSpace(accountPolicy.Value.EmailDomainList)
               || accountPolicy.Value.EmailDomainList
                   .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Any(d => d.Equals(mailDomain, StringComparison.InvariantCulture));
    }

    /// <summary>
    /// User password recovery request
    /// </summary>
    /// <remarks>
    /// Use this API to request password recovery. Sends an email to the user. Email URL: /reset
    /// </remarks>
    /// <param name="model"></param>
    /// <param name="token"></param>
    /// <response code="200">Password reset email sent successfully</response>
    /// <response code="400">Validation failed</response>
    /// <response code="404">User does not exist</response>
    [HttpPost]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Register))]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Recovery([FromBody] RecoveryModel model, CancellationToken token = default)
    {
        if (accountPolicy.Value.UseCaptcha && !await captcha.VerifyAsync(model, HttpContext, token))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_TokenValidationFailed)]));

        if (!accountPolicy.Value.EmailConfirmationRequired)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_ResetPasswordFromAdmin)]));

        var user = await userManager.FindByEmailAsync(model.Email!);
        if (user is null || !user.EmailConfirmed)
            return RecoveryAccepted();

        logger.Log(StaticLocalizer[nameof(Resources.Program.Account_SendEmailVerification)], HttpContext,
            TaskStatus.Pending);

        var rToken = Codec.Base64.Encode(await userManager.GeneratePasswordResetTokenAsync(user));
        var link = GetEmailLink("reset", rToken, model.Email);

        if (environment.IsDevelopment())
        {
            logger.Log(link, user, TaskStatus.Pending, LogLevel.Debug);
        }
        else
        {
            if (!mailSender.SendResetPasswordUrl(user.UserName, user.Email, link, localizer, globalConfig))
            {
                logger.LogWarning("Password recovery email delivery failed for user {UserId}.", user.Id);
                return RecoveryAccepted();
            }
        }

        return RecoveryAccepted();
    }

    private OkObjectResult RecoveryAccepted() =>
        Ok(new RequestResponse(localizer[nameof(Resources.Program.Account_EmailSent)], StatusCodes.Status200OK));

    /// <summary>
    /// User password reset
    /// </summary>
    /// <remarks>
    /// Use this API to reset the password. Email verification code is required.
    /// </remarks>
    /// <param name="model"></param>
    /// <response code="200">Password reset successfully</response>
    /// <response code="400">Validation failed</response>
    [HttpPost]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Register))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PasswordReset([FromBody] PasswordResetModel model)
    {
        var password = configService.DecryptApiData(model.Password);
        if (string.IsNullOrWhiteSpace(password))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Model_PasswordRequired)]));

        var user = await userManager.FindByEmailAsync(Codec.Base64.Decode(model.Email));
        if (user is null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_InvalidEmail)]));

        user.UpdateByHttpContext(HttpContext);

        var token = Codec.Base64.Decode(model.RToken);
        var result = await userManager.ResetPasswordAsync(user, token, password);

        if (!result.Succeeded)
            return HandleIdentityError(result.Errors);

        logger.Log(StaticLocalizer[nameof(Resources.Program.Account_PasswordReset)], user, TaskStatus.Success);

        return Ok();
    }

    /// <summary>
    /// User email confirmation
    /// </summary>
    /// <remarks>
    /// Use this API to confirm email using the verification code.
    /// </remarks>
    /// <param name="model"></param>
    /// <response code="200">Email verified successfully</response>
    /// <response code="400">Validation failed</response>
    /// <response code="401">Email verification failed</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Verify([FromBody] AccountVerifyModel model)
    {
        var user = await userManager.FindByEmailAsync(Codec.Base64.Decode(model.Email));

        if (user is null || user.EmailConfirmed)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_InvalidEmail)]));

        var result = await userManager.ConfirmEmailAsync(user, Codec.Base64.Decode(model.Token));

        if (!result.Succeeded)
            return Unauthorized(new RequestResponse(
                localizer[nameof(Resources.Program.Account_EmailVerificationFailed)],
                StatusCodes.Status401Unauthorized));

        logger.Log(StaticLocalizer[nameof(Resources.Program.Account_EmailVerified)], user, TaskStatus.Success);
        await signInManager.SignInAsync(user, true);

        user.LastSignedInUtc = DateTimeOffset.UtcNow;
        user.LastVisitedUtc = DateTimeOffset.UtcNow;
        user.RegisterTimeUtc = DateTimeOffset.UtcNow;

        result = await userManager.UpdateAsync(user);

        if (!result.Succeeded)
            return HandleIdentityError(result.Errors);

        return Ok();
    }

    /// <summary>
    /// User login
    /// </summary>
    /// <remarks>
    /// Use this API to log in to the account.
    /// </remarks>
    /// <param name="model"></param>
    /// <param name="token"></param>
    /// <response code="200">Login successful</response>
    /// <response code="400">Validation failed</response>
    /// <response code="401">Incorrect username or password</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LogIn([FromBody] LoginModel model, CancellationToken token = default)
    {
        if (accountPolicy.Value.UseCaptcha && !await captcha.VerifyAsync(model, HttpContext, token))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_TokenValidationFailed)]));

        var password = configService.DecryptApiData(model.Password);
        if (string.IsNullOrWhiteSpace(password))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Model_PasswordRequired)]));

        var user = await userManager.FindByNameAsync(model.UserName);
        user ??= await userManager.FindByEmailAsync(model.UserName);

        if (user is null)
            return Unauthorized(new RequestResponse(
                localizer[nameof(Resources.Program.Account_IncorrectUserNameOrPassword)],
                StatusCodes.Status401Unauthorized));

        if (user.Role == Role.Banned)
            return Unauthorized(new RequestResponse(localizer[nameof(Resources.Program.Account_UserDisabled)],
                StatusCodes.Status401Unauthorized));

        user.LastSignedInUtc = DateTimeOffset.UtcNow;
        user.UpdateByHttpContext(HttpContext);

        await signInManager.SignOutAsync();
        var result = await signInManager.PasswordSignInAsync(user, password, true, false);

        if (!result.Succeeded)
            return Unauthorized(new RequestResponse(
                localizer[nameof(Resources.Program.Account_IncorrectUserNameOrPassword)],
                StatusCodes.Status401Unauthorized));

        logger.Log(StaticLocalizer[nameof(Resources.Program.Account_UserLogined)], user, TaskStatus.Success);

        return Ok();
    }

    /// <summary>
    /// User logout
    /// </summary>
    /// <remarks>
    /// Use this API to log out of the account. User permissions required.
    /// </remarks>
    /// <response code="200">Logged out successfully</response>
    /// <response code="401">Unauthorized</response>
    [HttpPost]
    [RequireUser]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> LogOut()
    {
        if (await userManager.GetUserAsync(User) is { } user)
            await userManager.UpdateSecurityStampAsync(user);

        await signInManager.SignOutAsync();

        return Ok();
    }

    /// <summary>
    /// User data update
    /// </summary>
    /// <remarks>
    /// Use this API to update username and description. User permissions required.
    /// </remarks>
    /// <param name="model"></param>
    /// <response code="200">User data updated successfully</response>
    /// <response code="400">Validation failed or user data update failed</response>
    /// <response code="401">Unauthorized</response>
    [HttpPut]
    [RequireUser]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update([FromBody] ProfileUpdateModel model)
    {
        var user = await userManager.GetUserAsync(User);

        if (model.UserName is not null && model.UserName != user!.UserName)
        {
            var oldName = user.UserName;

            var unameRes = await userManager.SetUserNameAsync(user, model.UserName);

            if (!unameRes.Succeeded)
                return HandleIdentityError(unameRes.Errors);

            logger.Log(StaticLocalizer[nameof(Resources.Program.Account_UserUpdated), oldName!, user.UserName!],
                user, TaskStatus.Success);
        }

        user!.UpdateUserInfo(model);
        var result = await userManager.UpdateAsync(user);

        if (!result.Succeeded)
            return HandleIdentityError(result.Errors);

        return Ok();
    }

    /// <summary>
    /// User password change
    /// </summary>
    /// <remarks>
    /// Use this API to change user's password. User permissions required.
    /// </remarks>
    /// <param name="model"></param>
    /// <response code="200">Password changed successfully</response>
    /// <response code="400">Validation failed or password change failed</response>
    /// <response code="401">Unauthorized</response>
    [HttpPut]
    [RequireUser]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword([FromBody] PasswordChangeModel model)
    {
        var user = await userManager.GetUserAsync(User);

        var oldPassword = configService.DecryptApiData(model.Old);
        if (string.IsNullOrWhiteSpace(oldPassword))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Model_OldPasswordRequired)]));

        var newPassword = configService.DecryptApiData(model.New);
        if (string.IsNullOrWhiteSpace(newPassword))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Model_NewPasswordRequired)]));

        var result = await userManager.ChangePasswordAsync(user!, oldPassword, newPassword);

        if (!result.Succeeded)
            return HandleIdentityError(result.Errors);

        logger.Log(StaticLocalizer[nameof(Resources.Program.Account_PasswordChanged)], user,
            TaskStatus.Success);

        return Ok();
    }

    /// <summary>
    /// User email change
    /// </summary>
    /// <remarks>
    /// Use this API to change user's email. User permissions required. Email URL: /confirm
    /// </remarks>
    /// <param name="model"></param>
    /// <response code="200">Email change email sent successfully. Boolean indicates whether email verification is required</response>
    /// <response code="400">Validation failed or email already in use</response>
    /// <response code="401">Unauthorized</response>
    [HttpPut]
    [RequireUser]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Register))]
    [ProducesResponseType(typeof(RequestResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangeEmail([FromBody] MailChangeModel model)
    {
        if (await userManager.FindByEmailAsync(model.NewMail) is not null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_EmailUsed)]));

        if (!VerifyEmailDomain(model.NewMail))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_AvailableEmailDomain),
                accountPolicy.Value.EmailDomainList]));

        var user = await userManager.GetUserAsync(User);

        if (!accountPolicy.Value.EmailConfirmationRequired)
            return BadRequest(
                new RequestResponse<bool>(localizer[nameof(Resources.Program.Account_ChangeEmailFromAdmin)], false));

        logger.Log(StaticLocalizer[nameof(Resources.Program.Account_SendEmailChange)], user,
            TaskStatus.Pending);

        var token = Codec.Base64.Encode(await userManager.GenerateChangeEmailTokenAsync(user!, model.NewMail));
        var link = GetEmailLink("confirm", token, model.NewMail);

        if (environment.IsDevelopment())
        {
            logger.Log(link, user, TaskStatus.Pending, LogLevel.Debug);
        }
        else
        {
            if (!mailSender.SendChangeEmailUrl(user!.UserName, model.NewMail, link, localizer, globalConfig))
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_EmailSendFailed)]));
        }

        return Ok(new RequestResponse<bool>(localizer[nameof(Resources.Program.Account_EmailVerificationPending)], true,
            StatusCodes.Status200OK));
    }

    /// <summary>
    /// User email change confirmation
    /// </summary>
    /// <remarks>
    /// Use this API to confirm email change. Email verification code required. User permissions required.
    /// </remarks>
    /// <param name="model"></param>
    /// <response code="200">Email changed successfully</response>
    /// <response code="400">Validation failed or invalid email</response>
    /// <response code="401">Unauthorized</response>
    /// <response code="403">Forbidden</response>
    [HttpPost]
    [RequireUser]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MailChangeConfirm([FromBody] AccountVerifyModel model)
    {
        var user = await userManager.GetUserAsync(User);
        var result = await userManager.ChangeEmailAsync(user!, Codec.Base64.Decode(model.Email),
            Codec.Base64.Decode(model.Token));

        if (!result.Succeeded)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_InvalidEmail)]));

        logger.Log(StaticLocalizer[nameof(Resources.Program.Account_EmailChanged)], user, TaskStatus.Success);

        return Ok();
    }

    /// <summary>
    /// Get user information
    /// </summary>
    /// <remarks>
    /// Use this API to get user information. User permissions required.
    /// </remarks>
    /// <response code="200">User information retrieved successfully</response>
    /// <response code="401">Unauthorized</response>
    [HttpGet]
    [RequireUser]
    [ProducesResponseType(typeof(ProfileUserInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Profile()
    {
        var user = await userManager.GetUserAsync(User);

        return Ok(ProfileUserInfoModel.FromUserInfo(user!));
    }

    /// <summary>
    /// Update user avatar
    /// </summary>
    /// <remarks>
    /// Use this API to update user's avatar. User permissions required.
    /// </remarks>
    /// <response code="200">User avatar URL</response>
    /// <response code="400">Invalid request</response>
    /// <response code="401">Unauthorized</response>
    [HttpPut]
    [RequireUser]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Avatar(IFormFile file, CancellationToken token)
    {
        switch (file.Length)
        {
            case 0:
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_SizeZero)]));
            case > 3 * 1024 * 1024:
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_SizeTooLarge)]));
        }

        var user = await userManager.GetUserAsync(User);

        await using var transaction = await blobService.BeginTransactionAsync(token);
        if (user!.AvatarHash is not null)
            await blobService.DeleteBlobByHash(user.AvatarHash, token);

        var avatar = await blobService.CreateOrUpdateImage(file, "avatar", 300, token);

        if (avatar is null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Avatar_UpdateFailed)]));

        user.AvatarHash = avatar.Hash;
        var result = await userManager.UpdateAsync(user);

        if (result != IdentityResult.Success)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_UserUpdateFailed)]));

        await transaction.CommitAsync(token);
        logger.Log(StaticLocalizer[nameof(Resources.Program.Account_AvatarUpdated), avatar.Hash[..8]], user,
            TaskStatus.Success);

        return Ok(avatar.Url());
    }

    private async Task<(UserInfo? User, IEnumerable<IdentityError>? Errors)> FindOrCreatePortalUser(
        PortalSsoUser portalUser,
        string loginProvider,
        string providerKey)
    {
        var userName = BuildPortalUserName(portalUser.Id);
        var email = BuildPortalEmail(portalUser.Id);
        var user = new UserInfo
        {
            UserName = userName,
            Email = email,
            EmailConfirmed = true,
            RegisterTimeUtc = DateTimeOffset.UtcNow,
            LastSignedInUtc = DateTimeOffset.UtcNow,
            Role = MapPortalRole(portalUser.RoleCode)
        };
        UpdatePortalUserInfo(user, portalUser);
        user.UpdateByHttpContext(HttpContext);

        var createResult = await userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            var linkedUser = await userManager.FindByLoginAsync(loginProvider, providerKey);
            if (linkedUser is not null)
                return (linkedUser, null);

            return (null, createResult.Errors);
        }

        return await EnsurePortalLogin(user, loginProvider, providerKey);
    }

    private async Task<(UserInfo? User, IEnumerable<IdentityError>? Errors)> EnsurePortalLogin(
        UserInfo user,
        string loginProvider,
        string providerKey)
    {
        var logins = await userManager.GetLoginsAsync(user);
        if (logins.Any(login => login.LoginProvider == loginProvider && login.ProviderKey == providerKey))
            return (user, null);

        var loginResult = await userManager.AddLoginAsync(user,
            new UserLoginInfo(loginProvider, providerKey, "Portal IAM"));

        if (loginResult.Succeeded)
            return (user, null);

        // A concurrent request may have created the binding after the first lookup.
        var linkedUser = await userManager.FindByLoginAsync(loginProvider, providerKey);
        return linkedUser is not null ? (linkedUser, null) : (null, loginResult.Errors);
    }

    private void UpdatePortalUserInfo(UserInfo user, PortalSsoUser portalUser)
    {
        user.RealName = TruncateUserData(string.IsNullOrWhiteSpace(portalUser.RealName)
            ? portalUser.UserName
            : portalUser.RealName);
        user.Role = MapPortalRole(portalUser.RoleCode);
    }

    private string BuildPortalUserName(int portalUserId)
    {
        var suffix = portalUserId.ToString(CultureInfo.InvariantCulture);
        var userName = $"iam_{suffix}";

        return userName.Length <= Limits.MaxUserNameLength
            ? userName
            : $"iam_{suffix[^Math.Min(11, suffix.Length)..]}";
    }

    private string BuildPortalEmail(int portalUserId)
    {
        var domain = portalSsoConfig.Value.DefaultEmailDomain.Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(domain) || domain.Contains('@', StringComparison.Ordinal))
            domain = "sso.local";

        return $"portal-{portalUserId.ToString(CultureInfo.InvariantCulture)}@{domain}";
    }

    private static string NormalizeLoginProvider(string? loginProvider) =>
        string.IsNullOrWhiteSpace(loginProvider) ? "PortalIAM" : loginProvider.Trim();

    private static string? NormalizePortalReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return "/";

        returnUrl = returnUrl.Trim();

        if (!returnUrl.StartsWith("/", StringComparison.Ordinal) ||
            returnUrl.StartsWith("//", StringComparison.Ordinal) ||
            returnUrl.Contains('\\'))
            return null;

        return returnUrl;
    }

    private static Role MapPortalRole(string roleCode) =>
        roleCode.Trim().ToLowerInvariant() switch
        {
            "super_admin" => Role.SuperAdmin,
            "ctf_admin" => Role.Admin,
            "teacher" => Role.Teacher,
            "student" => Role.Student,
            _ => Role.Student
        };

    private static string TruncateUserData(string? value)
    {
        value = value?.Trim() ?? string.Empty;
        return value.Length <= Limits.MaxUserDataLength ? value : value[..Limits.MaxUserDataLength];
    }

    private string GetEmailLink(string action, string token, string? email)
        => $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}/account/{action}?" +
           $"token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(Codec.Base64.Encode(email))}";

    private BadRequestObjectResult HandleIdentityError(IEnumerable<IdentityError> errors) =>
        BadRequest(new RequestResponse(errors.FirstOrDefault()?.Description ??
                                       localizer[nameof(Resources.Program.Identity_UnknownError)]));
}
