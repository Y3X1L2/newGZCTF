using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace GZCTF.Infrastructure.Cache;

public enum RedisRuntimeMode
{
    Disabled,
    SingleInstance,
    Distributed
}

public sealed class RedisRuntimeOptions
{
    public const string SectionName = "RedisRuntime";

    public RedisRuntimeMode Mode { get; set; } = RedisRuntimeMode.Disabled;
    public string? ConnectionString { get; set; }
    public string KeyPrefix { get; set; } = "gzctf";
    public string ClientName { get; set; } = "gzctf";
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan StreamLagWarningThreshold { get; set; } = TimeSpan.FromSeconds(2);
    public int ApplicationInstanceCount { get; set; } = 1;
}

public sealed partial class RedisRuntimeOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<RedisRuntimeOptions>
{
    public ValidateOptionsResult Validate(string? name, RedisRuntimeOptions options) =>
        Validate(options, environment.IsProduction());

    internal static ValidateOptionsResult Validate(RedisRuntimeOptions options, bool isProduction)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (!Enum.IsDefined(options.Mode))
            failures.Add("Redis runtime mode is invalid.");

        if (string.IsNullOrWhiteSpace(options.KeyPrefix) || !KeyPrefixPattern().IsMatch(options.KeyPrefix))
            failures.Add("Redis key prefix must contain only lowercase letters, numbers, and hyphens.");

        if (string.IsNullOrWhiteSpace(options.ClientName) || options.ClientName.Length > 64 ||
            options.ClientName.Any(char.IsWhiteSpace))
            failures.Add("Redis client name must be non-empty, at most 64 characters, and contain no whitespace.");

        if (options.ConnectTimeout <= TimeSpan.Zero || options.ConnectTimeout.TotalMilliseconds > int.MaxValue)
            failures.Add("Redis connect timeout must be a positive finite value.");

        if (options.OperationTimeout <= TimeSpan.Zero || options.OperationTimeout.TotalMilliseconds > int.MaxValue)
            failures.Add("Redis operation timeout must be a positive finite value.");

        if (options.StreamLagWarningThreshold <= TimeSpan.Zero)
            failures.Add("Redis stream lag warning threshold must be positive.");

        if (options.ApplicationInstanceCount < 1)
            failures.Add("Application instance count must be at least one.");

        if (options.Mode == RedisRuntimeMode.Distributed && string.IsNullOrWhiteSpace(options.ConnectionString))
            failures.Add("Distributed Redis mode requires a connection string.");

        if (isProduction && options.ApplicationInstanceCount > 1 &&
            options.Mode == RedisRuntimeMode.SingleInstance)
            failures.Add("SingleInstance Redis mode cannot be used by a multi-instance production deployment.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$")]
    private static partial Regex KeyPrefixPattern();
}
