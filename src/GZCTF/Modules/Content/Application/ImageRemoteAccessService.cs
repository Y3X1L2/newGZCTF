using System.Security.Cryptography;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Content.Application;

public sealed record ImageRemoteAccessModel(
    bool Enabled,
    TeamLabRemoteProtocol Protocol,
    int Port,
    string? Username,
    RemoteCredentialMode CredentialMode,
    bool HasCredential,
    DateTimeOffset? UpdatedAt);

public sealed record UpdateImageRemoteAccessModel(
    bool Enabled,
    TeamLabRemoteProtocol Protocol,
    int Port,
    string? Username,
    RemoteCredentialMode CredentialMode,
    string? Credential);

public sealed record CompetitionWindowsRdpProfile(
    int ImageTemplateId,
    int Port,
    string Username,
    string Password);

public sealed class ImageRemoteAccessService(
    AppDbContext context,
    IDataProtectionProvider protectionProvider,
    ILogger<ImageRemoteAccessService>? logger = null)
{
    private readonly IDataProtector _protector = protectionProvider.CreateProtector("GZCTF.TeamLab.ImageRemoteAccess.v1");

    public async Task<ImageRemoteAccessModel> GetAsync(int imageTemplateId, CancellationToken cancellationToken)
    {
        var configuration = await context.ImageTemplateRemoteAccesses.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ImageTemplateId == imageTemplateId, cancellationToken);
        return configuration is null
            ? new ImageRemoteAccessModel(false, TeamLabRemoteProtocol.Ssh, 22, null,
                RemoteCredentialMode.PlatformGenerated, false, null)
            : ToModel(configuration);
    }

    public async Task<bool> HasCompetitionWindowsRdpProfileAsync(
        int imageTemplateId,
        CancellationToken cancellationToken) =>
        await GetCompetitionWindowsRdpProfileAsync(imageTemplateId, cancellationToken) is not null;

    public async Task<CompetitionWindowsRdpProfile?> GetCompetitionWindowsRdpProfileAsync(
        int imageTemplateId,
        CancellationToken cancellationToken)
    {
        var configuration = await LoadCompetitionWindowsConfigurationAsync(imageTemplateId, cancellationToken);
        if (!IsCompetitionWindowsRdpConfiguration(configuration))
            return null;

        try
        {
            return new CompetitionWindowsRdpProfile(
                imageTemplateId,
                configuration!.Port,
                configuration.Username!.Trim(),
                RevealSecret(configuration));
        }
        catch (CryptographicException exception)
        {
            logger?.LogWarning(exception,
                "Unable to decrypt remote access credential for image template {ImageTemplateId}",
                imageTemplateId);
            return null;
        }
    }

    public async Task<ImageRemoteAccessModel> UpdateAsync(
        ImageTemplate template,
        UpdateImageRemoteAccessModel request,
        CancellationToken cancellationToken)
    {
        Validate(template, request);
        var configuration = await context.ImageTemplateRemoteAccesses
            .SingleOrDefaultAsync(item => item.ImageTemplateId == template.Id, cancellationToken);
        if (configuration is null)
        {
            configuration = new ImageTemplateRemoteAccess { ImageTemplateId = template.Id };
            context.ImageTemplateRemoteAccesses.Add(configuration);
        }
        configuration.Enabled = request.Enabled;
        configuration.Protocol = request.Protocol;
        configuration.Port = request.Port;
        configuration.Username = string.IsNullOrWhiteSpace(request.Username) ? null : request.Username.Trim();
        configuration.CredentialMode = request.CredentialMode;
        if (request.CredentialMode == RemoteCredentialMode.PlatformGenerated)
            configuration.ProtectedSecret = null;
        else if (!string.IsNullOrWhiteSpace(request.Credential))
            configuration.ProtectedSecret = _protector.Protect(request.Credential);
        else if (request.Enabled && string.IsNullOrWhiteSpace(configuration.ProtectedSecret))
            throw new InvalidOperationException("An existing-account remote access configuration requires a password or private key.");
        configuration.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return ToModel(configuration);
    }

    public string RevealSecret(ImageTemplateRemoteAccess configuration) =>
        string.IsNullOrWhiteSpace(configuration.ProtectedSecret)
            ? throw new InvalidOperationException("The image remote access configuration has no credential.")
            : _protector.Unprotect(configuration.ProtectedSecret);

    private Task<ImageTemplateRemoteAccess?> LoadCompetitionWindowsConfigurationAsync(
        int imageTemplateId,
        CancellationToken cancellationToken) =>
        context.ImageTemplates.AsNoTracking()
            .Where(template =>
                template.Id == imageTemplateId &&
                template.OSType == OSType.Windows &&
                template.ImageType != ImageType.Docker &&
                template.Status == ImageStatus.Ready)
            .Select(template => template.RemoteAccess)
            .SingleOrDefaultAsync(cancellationToken);

    internal static bool IsCompetitionWindowsRdpConfiguration(ImageTemplateRemoteAccess? configuration) =>
        configuration is
        {
            Enabled: true,
            Protocol: TeamLabRemoteProtocol.Rdp,
            CredentialMode: RemoteCredentialMode.ExistingAccount,
            Port: >= 1 and <= 65535
        } &&
        !string.IsNullOrWhiteSpace(configuration.Username) &&
        !string.IsNullOrWhiteSpace(configuration.ProtectedSecret);

    private static ImageRemoteAccessModel ToModel(ImageTemplateRemoteAccess item) => new(
        item.Enabled, item.Protocol, item.Port, item.Username, item.CredentialMode,
        !string.IsNullOrWhiteSpace(item.ProtectedSecret), item.UpdatedAt);

    private static void Validate(ImageTemplate template, UpdateImageRemoteAccessModel request)
    {
        if (request.Port is < 1 or > 65535) throw new InvalidOperationException("The remote access port is invalid.");
        if (template.ImageType == ImageType.Docker && request.Protocol != TeamLabRemoteProtocol.ContainerTerminal)
            throw new InvalidOperationException("Docker images only support the platform web terminal.");
        if (template.ImageType != ImageType.Docker && request.Protocol == TeamLabRemoteProtocol.ContainerTerminal)
            throw new InvalidOperationException("Virtual machine images require SSH or RDP remote access.");
        if (request.Protocol == TeamLabRemoteProtocol.Rdp && template.OSType != OSType.Windows)
            throw new InvalidOperationException("RDP remote access requires a Windows image.");
        if (request.Protocol == TeamLabRemoteProtocol.Ssh && template.OSType != OSType.Linux)
            throw new InvalidOperationException("SSH remote access requires a Linux image.");
        if (request.Enabled && request.CredentialMode == RemoteCredentialMode.ExistingAccount &&
            string.IsNullOrWhiteSpace(request.Username))
            throw new InvalidOperationException("An existing-account remote access configuration requires a username.");
    }
}
