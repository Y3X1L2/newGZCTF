using GZCTF.Models.Internal;

namespace GZCTF.Models.Request.Account;

/// <summary>
/// Public account capabilities used to compose authentication pages.
/// </summary>
public class AccountCapabilitiesModel
{
    /// <summary>
    /// Whether local username and password login is available.
    /// </summary>
    public bool AllowPasswordLogin { get; set; } = true;

    /// <summary>
    /// Whether self-service account registration is available.
    /// </summary>
    public bool AllowRegister { get; set; }

    /// <summary>
    /// Whether password recovery by email is available.
    /// </summary>
    public bool PasswordRecoveryAvailable { get; set; }

    /// <summary>
    /// Whether new accounts require email confirmation.
    /// </summary>
    public bool EmailConfirmationRequired { get; set; }

    /// <summary>
    /// Unified identity portal entry shown by the login page.
    /// </summary>
    public PortalSsoCapabilityModel PortalSso { get; set; } = new();

    public static AccountCapabilitiesModel FromConfig(AccountPolicy accountPolicy, PortalSsoConfig portalSso)
    {
        var entryUrl = NormalizePortalEntryUrl(portalSso.EntryUrl);

        return new()
        {
            AllowRegister = accountPolicy.AllowRegister,
            PasswordRecoveryAvailable = accountPolicy.EmailConfirmationRequired,
            EmailConfirmationRequired = accountPolicy.EmailConfirmationRequired,
            PortalSso = new()
            {
                Enabled = portalSso.Enabled && entryUrl is not null,
                EntryUrl = portalSso.Enabled ? entryUrl : null
            }
        };
    }

    internal static string? NormalizePortalEntryUrl(string? entryUrl)
    {
        if (!Uri.TryCreate(entryUrl, UriKind.Absolute, out var uri))
            return null;

        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps ? uri.AbsoluteUri : null;
    }
}

public class PortalSsoCapabilityModel
{
    public bool Enabled { get; set; }

    public string? EntryUrl { get; set; }
}
