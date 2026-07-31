using System.Security.Cryptography;
using GZCTF.Models;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabRemoteCredentialService(AppDbContext context, IDataProtectionProvider protectionProvider)
{
    private readonly IDataProtector _protector = protectionProvider.CreateProtector("GZCTF.TeamLab.RuntimeRemoteCredential.v1");

    public async Task<TeamLabRuntimeRemoteCredential> EnsurePlatformCredentialAsync(
        int runtimeId,
        int generation,
        int assetId,
        TeamLabRemoteProtocol protocol,
        CancellationToken cancellationToken)
    {
        var current = await context.TeamLabRuntimeRemoteCredentials.SingleOrDefaultAsync(item =>
            item.RuntimeId == runtimeId && item.Generation == generation && item.RuntimeAssetId == assetId &&
            item.Protocol == protocol, cancellationToken);
        if (current is not null) return current;

        var username = protocol == TeamLabRemoteProtocol.Rdp
            ? $"gzops{assetId}"
            : "gzops";
        current = new TeamLabRuntimeRemoteCredential
        {
            RuntimeId = runtimeId,
            Generation = generation,
            RuntimeAssetId = assetId,
            Protocol = protocol,
            Username = username,
            ProtectedSecret = _protector.Protect(GenerateSecret()),
            Mode = RemoteCredentialMode.PlatformGenerated
        };
        context.TeamLabRuntimeRemoteCredentials.Add(current);
        await context.SaveChangesAsync(cancellationToken);
        return current;
    }

    public string RevealSecret(TeamLabRuntimeRemoteCredential credential) =>
        string.IsNullOrWhiteSpace(credential.ProtectedSecret)
            ? throw new InvalidOperationException("The runtime remote credential has no secret.")
            : _protector.Unprotect(credential.ProtectedSecret);

    public async Task RevokeGenerationAsync(int runtimeId, int generation, CancellationToken cancellationToken)
    {
        var credentials = await context.TeamLabRuntimeRemoteCredentials
            .Where(item => item.RuntimeId == runtimeId && item.Generation == generation && item.RevokedAt == null)
            .ToArrayAsync(cancellationToken);
        foreach (var credential in credentials) credential.RevokedAt = DateTimeOffset.UtcNow;
        if (credentials.Length > 0) await context.SaveChangesAsync(cancellationToken);
    }

    private static string GenerateSecret()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return $"Aa1!{Convert.ToHexStringLower(bytes)}";
    }
}
