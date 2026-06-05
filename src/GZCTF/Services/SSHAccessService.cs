using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Models;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services;

/// <summary>
/// Generates and manages temporary SSH credentials for IR challenge instances.
/// Credentials are scoped to a specific instance and rotated on each environment reset.
/// </summary>
public class SSHAccessService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    private readonly ILogger<SSHAccessService> _logger;
    private const int TokenLength = 32;

    public SSHAccessService(AppDbContext context, IConfiguration config, ILogger<SSHAccessService> logger)
    {
        _context = context;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Generate temporary SSH credentials for an IR instance.
    /// </summary>
    /// <param name="instanceId">The IR instance ID</param>
    /// <returns>SSH credentials including username, password, host, and port</returns>
    public async Task<SshCredentials> GenerateCredentialsAsync(Guid instanceId)
    {
        _logger.LogInformation("Generating SSH credentials for IR instance {InstanceId}", instanceId);

        var instance = await _context.IRInstances
            .Include(i => i.Challenge)
            .FirstOrDefaultAsync(i => i.Id == instanceId);

        if (instance is null)
            throw new InvalidOperationException($"IR instance {instanceId} not found.");

        var username = $"player-{instanceId:N}"[..16];
        var password = GenerateSecureToken();
        var host = _config["SshSettings:GatewayHost"] ?? "localhost";
        var port = _config.GetValue("SshSettings:GatewayPort", 2222);

        var credentials = new SshCredentials
        {
            Username = username,
            Password = password,
            Host = host,
            Port = port,
            InstanceId = instanceId
        };

        // Store credentials in the instance's AccessDetails
        var accessDetails = string.IsNullOrEmpty(instance.AccessDetails)
            ? new Dictionary<string, object?>()
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(instance.AccessDetails) ?? [];

        accessDetails["SshHost"] = host;
        accessDetails["SshPort"] = port;
        accessDetails["SshUsername"] = username;
        accessDetails["SshPasswordHash"] = HashPassword(password);

        instance.AccessDetails = JsonSerializer.Serialize(accessDetails);
        await _context.SaveChangesAsync();

        _logger.LogInformation("SSH credentials generated for IR instance {InstanceId}: {Username}", instanceId, username);

        return credentials;
    }

    /// <summary>
    /// Rotate SSH credentials for an IR instance on environment reset.
    /// Generates a new password while keeping the same username pattern.
    /// </summary>
    /// <param name="instanceId">The IR instance ID</param>
    /// <returns>New SSH credentials</returns>
    public async Task<SshCredentials> RotateCredentialsAsync(Guid instanceId)
    {
        _logger.LogInformation("Rotating SSH credentials for IR instance {InstanceId}", instanceId);

        var instance = await _context.IRInstances
            .FirstOrDefaultAsync(i => i.Id == instanceId);

        if (instance is null)
            throw new InvalidOperationException($"IR instance {instanceId} not found.");

        var newPassword = GenerateSecureToken();
        var host = _config["SshSettings:GatewayHost"] ?? "localhost";
        var port = _config.GetValue("SshSettings:GatewayPort", 2222);
        var username = $"player-{instanceId:N}"[..16];

        // Update credentials in AccessDetails
        var accessDetails = string.IsNullOrEmpty(instance.AccessDetails)
            ? new Dictionary<string, object?>()
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(instance.AccessDetails) ?? [];

        accessDetails["SshHost"] = host;
        accessDetails["SshPort"] = port;
        accessDetails["SshUsername"] = username;
        accessDetails["SshPasswordHash"] = HashPassword(newPassword);

        instance.AccessDetails = JsonSerializer.Serialize(accessDetails);
        await _context.SaveChangesAsync();

        _logger.LogInformation("SSH credentials rotated for IR instance {InstanceId}", instanceId);

        return new SshCredentials
        {
            Username = username,
            Password = newPassword,
            Host = host,
            Port = port,
            InstanceId = instanceId
        };
    }

    /// <summary>
    /// Get the current SSH credentials (password hash) for an instance.
    /// </summary>
    public async Task<SshCredentials?> GetCredentialsAsync(Guid instanceId)
    {
        var instance = await _context.IRInstances
            .FirstOrDefaultAsync(i => i.Id == instanceId);

        if (instance?.AccessDetails is null)
            return null;

        var details = JsonSerializer.Deserialize<Dictionary<string, object?>>(instance.AccessDetails);
        if (details is null)
            return null;

        return new SshCredentials
        {
            Username = details.GetValueOrDefault("SshUsername")?.ToString() ?? string.Empty,
            Password = string.Empty, // Password hash is stored, not plaintext
            Host = details.GetValueOrDefault("SshHost")?.ToString() ?? "localhost",
            Port = details.TryGetValue("SshPort", out var p) && p is JsonElement e && e.TryGetInt32(out var port) ? port : 2222,
            InstanceId = instanceId
        };
    }

    /// <summary>
    /// Verify that the provided password matches the stored hash for an instance.
    /// </summary>
    public async Task<bool> VerifyPasswordAsync(Guid instanceId, string password)
    {
        var instance = await _context.IRInstances
            .FirstOrDefaultAsync(i => i.Id == instanceId);

        if (instance?.AccessDetails is null)
            return false;

        var details = JsonSerializer.Deserialize<Dictionary<string, object?>>(instance.AccessDetails);
        if (details is null)
            return false;

        var storedHash = details.GetValueOrDefault("SshPasswordHash")?.ToString();
        if (string.IsNullOrEmpty(storedHash))
            return false;

        return VerifyPasswordHash(password, storedHash);
    }

    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenLength);
        return Convert.ToBase64String(bytes)[..TokenLength]
            .Replace('/', '_')
            .Replace('+', '-');
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations: 100_000,
            HashAlgorithmName.SHA256,
            outputLength: 32);

        var hashBytes = new byte[48];
        Array.Copy(salt, 0, hashBytes, 0, 16);
        Array.Copy(hash, 0, hashBytes, 16, 32);
        return Convert.ToBase64String(hashBytes);
    }

    private static bool VerifyPasswordHash(string password, string storedHash)
    {
        try
        {
            var hashBytes = Convert.FromBase64String(storedHash);
            if (hashBytes.Length != 48)
                return false;

            var salt = hashBytes[..16];
            var storedPasswordHash = hashBytes[16..];

            var computedHash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                iterations: 100_000,
                HashAlgorithmName.SHA256,
                outputLength: 32);

            return CryptographicOperations.FixedTimeEquals(storedPasswordHash, computedHash);
        }
        catch
        {
            return false;
        }
    }

}

/// <summary>
/// SSH credentials for an IR challenge instance.
/// </summary>
public class SshCredentials
{
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 2222;
    public Guid InstanceId { get; init; }
}
