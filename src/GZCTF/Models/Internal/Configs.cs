using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using GZCTF.Extensions;
using GZCTF.Infrastructure.Cache;
using GZCTF.TeamLab.Contracts;
using MemoryPack;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Utilities.Encoders;
using Serilog.Sinks.Grafana.Loki;

namespace GZCTF.Models.Internal;

/// <summary>
/// Ignore when saving automatically
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AutoSaveIgnoreAttribute : Attribute;

/// <summary>
/// Update cache when this property changes
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class CacheFlushAttribute(string cacheKey) : Attribute
{
    public string CacheKey { get; } = cacheKey;
}

/// <summary>
/// Account policy
/// </summary>
public class AccountPolicy
{
    /// <summary>
    /// Allow user registration
    /// </summary>
    public bool AllowRegister { get; set; } = true;

    /// <summary>
    /// Activate account upon registration
    /// </summary>
    public bool ActiveOnRegister { get; set; } = true;

    /// <summary>
    /// Use captcha verification
    /// </summary>
    [CacheFlush(CachePolicyNames.CaptchaConfig)]
    public bool UseCaptcha { get; set; }

    /// <summary>
    /// Email confirmation required for registration, email change, and password recovery
    /// </summary>
    public bool EmailConfirmationRequired { get; set; }

    /// <summary>
    /// Email domain list, separated by commas
    /// </summary>
    public string EmailDomainList { get; set; } = string.Empty;
}

/// <summary>
/// Portal single sign-on configuration
/// </summary>
public class PortalSsoConfig
{
    /// <summary>
    /// Enable login through the external portal IAM service.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// User-facing portal entry used to start an IAM login flow.
    /// The portal remains responsible for issuing the token passed to the callback.
    /// </summary>
    public string EntryUrl { get; set; } = string.Empty;

    /// <summary>
    /// IAM profile endpoint, for example http://192.168.20.150:8001/iam/v1/auth/profile.
    /// </summary>
    public string ProfileEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Local email domain used when IAM does not provide an email.
    /// </summary>
    public string DefaultEmailDomain { get; set; } = "sso.local";

    /// <summary>
    /// Identity login provider name used for binding IAM users to local users.
    /// </summary>
    public string LoginProvider { get; set; } = "PortalIAM";

    /// <summary>
    /// Require the IAM profile to include the CTF platform entry.
    /// </summary>
    public bool RequireCtfPlatform { get; set; } = true;

    /// <summary>
    /// Platform code expected from IAM.
    /// </summary>
    public string CtfPlatformCode { get; set; } = "ctf-competition";

    /// <summary>
    /// Profile request timeout in seconds.
    /// </summary>
    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 8;

    /// <summary>
    /// Update local display fields and role on each portal login.
    /// </summary>
    public bool UpdateUserProfileOnLogin { get; set; } = true;
}

/// <summary>
/// Container policy
/// </summary>
public class ContainerPolicy
{
    /// <summary>
    /// Automatically destroy the oldest container when the limit is reached
    /// </summary>
    public bool AutoDestroyOnLimitReached { get; set; }

    /// <summary>
    /// User container limit, used to limit the number of exercise containers
    /// </summary>
    public int MaxExerciseContainerCountPerUser { get; set; } = 1;

    /// <summary>
    /// Default container lifetime in minutes
    /// </summary>
    [CacheFlush(CachePolicyNames.ClientConfig)]
    [Range(1, 7200, ErrorMessageResourceName = nameof(Resources.Program.Model_OutOfRange),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public int DefaultLifetime { get; set; } = 120;

    /// <summary>
    /// Extension duration for each renewal in minutes
    /// </summary>
    [CacheFlush(CachePolicyNames.ClientConfig)]
    [Range(1, 7200, ErrorMessageResourceName = nameof(Resources.Program.Model_OutOfRange),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public int ExtensionDuration { get; set; } = 120;

    /// <summary>
    /// Renewal window before container stops in minutes
    /// </summary>
    [CacheFlush(CachePolicyNames.ClientConfig)]
    [Range(1, 360, ErrorMessageResourceName = nameof(Resources.Program.Model_OutOfRange),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public int RenewalWindow { get; set; } = 10;
}

public class X25519KeyPair
{
    /// <summary>
    /// Public key
    /// </summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Private key
    /// </summary>
    public string PrivateKey { get; set; } = string.Empty;

    public void RegenerateKeys(byte[] xorKey)
    {
        var kp = CryptoUtils.GenerateX25519KeyPair();
        var privateKey = (X25519PrivateKeyParameters)kp.Private;
        var publicKey = (X25519PublicKeyParameters)kp.Public;
        var privateKeyBytes = Codec.Xor(privateKey.GetEncoded(), xorKey);
        PublicKey = Base64.ToBase64String(publicKey.GetEncoded());
        PrivateKey = Base64.ToBase64String(privateKeyBytes);
    }

    public string? Decrypt(string data, byte[] xorKey)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(xorKey);

        try
        {
            var encryptedData = Base64.Decode(data);
            var privateKeyBytes = Codec.Xor(Base64.Decode(PrivateKey), xorKey);
            var privateKey = new X25519PrivateKeyParameters(privateKeyBytes);

            return Encoding.UTF8.GetString(CryptoUtils.DecryptData(encryptedData, privateKey));
        }
        catch
        {
            // If decryption fails, return null
            return null;
        }
    }
}

public class Ed25519KeyPair
{
    /// <summary>
    /// Public key
    /// </summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Private key
    /// </summary>
    public string PrivateKey { get; set; } = string.Empty;

    public void RegenerateKeys(byte[] xorKey)
    {
        var kp = CryptoUtils.GenerateEd25519KeyPair();
        var privateKey = (Ed25519PrivateKeyParameters)kp.Private;
        var publicKey = (Ed25519PublicKeyParameters)kp.Public;
        var privateKeyBytes = Codec.Xor(privateKey.GetEncoded(), xorKey);
        PublicKey = Base64.ToBase64String(publicKey.GetEncoded());
        PrivateKey = Base64.ToBase64String(privateKeyBytes);
    }

    public string Sign(string data, byte[] xorKey, bool useUrlSafeBase64 = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(xorKey);

        var privateKeyBytes = Codec.Xor(Base64.Decode(PrivateKey), xorKey);
        var privateKey = new Ed25519PrivateKeyParameters(privateKeyBytes);
        return CryptoUtils.GenerateSignature(data, privateKey, SignAlgorithm.Ed25519, useUrlSafeBase64);
    }

    public bool Verify(string data, string signature, bool useUrlSafeBase64 = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(signature);

        try
        {
            var publicKey = new Ed25519PublicKeyParameters(Base64.Decode(PublicKey));
            return CryptoUtils.VerifySignature(data, signature, publicKey, SignAlgorithm.Ed25519, useUrlSafeBase64);
        }
        catch
        {
            // If verification fails, return false
            return false;
        }
    }
}

/// <summary>
/// A context for signature operations, including signing and verifying
/// </summary>
public record SignatureContext(Ed25519KeyPair EncryptedKeyPair, byte[] XorKey)
{
    public string Sign(string data, bool urlSafe = true) =>
        EncryptedKeyPair.Sign(data, XorKey, urlSafe);

    public bool Verify(string data, string signature, bool urlSafe = true) =>
        EncryptedKeyPair.Verify(data, signature, urlSafe);
}

/// <summary>
/// Configs controlled by the backend
/// </summary>
public class ManagedConfig
{
    /// <summary>
    /// Api encryption configuration
    /// </summary>
    public X25519KeyPair ApiEncryption { get; set; } = new();

}

/// <summary>
/// Global settings
/// </summary>
public class GlobalConfig
{
    /// <summary>
    /// Default site description
    /// </summary>
    public const string DefaultDescription =
        "YINYU CTF平台提供赛事管理、攻防演练、理论赛与分布式靶场调度能力。";

    public const string DefaultSlogan = "专业赛事管理与攻防演练平台";

    public static readonly string[] DefaultSlogans =
    [
        "演练赛事在线调度",
        "平台通知实时归档",
        "靶场服务安全编排"
    ];

    /// <summary>
    /// Platform prefix name
    /// </summary>
    [CacheFlush(CachePolicyNames.Index)]
    [CacheFlush(CachePolicyNames.ClientConfig)]
    public string Title { get; set; } = "YINYU";

    /// <summary>
    /// Platform slogan
    /// </summary>
    [CacheFlush(CachePolicyNames.ClientConfig)]
    public string Slogan { get; set; } = JoinSlogans(DefaultSlogans);

    /// <summary>
    /// Site description information
    /// </summary>
    [CacheFlush(CachePolicyNames.Index)]
    public string? Description { get; set; } = DefaultDescription;

    /// <summary>
    /// Footer information
    /// </summary>
    [CacheFlush(CachePolicyNames.ClientConfig)]
    public string? FooterInfo { get; set; }

    /// <summary>
    /// Custom theme color
    /// </summary>
    [CacheFlush(CachePolicyNames.ClientConfig)]
    public string? CustomTheme { get; set; }

    /// <summary>
    /// Use asymmetric encryption for API requests
    /// </summary>
    [CacheFlush(CachePolicyNames.ClientConfig)]
    public bool ApiEncryption { get; set; }

    /// <summary>
    /// Platform logo hash
    /// </summary>
    [AutoSaveIgnore]
    public string? LogoHash { get; set; }

    /// <summary>
    /// Platform favicon hash
    /// </summary>
    [AutoSaveIgnore]
    public string? FaviconHash { get; set; }

    [JsonIgnore]
    public string? LogoUrl => string.IsNullOrEmpty(LogoHash) ? null : $"/assets/{LogoHash}/logo";

    /// <summary>
    /// Platform name, used for email and homepage rendering
    /// </summary>
    [JsonIgnore]
    public string Platform => ToPlatformName(Title);

    public static string ToPlatformName(string? title)
    {
        var normalized = title?.Trim() ?? string.Empty;
        var compactTitle = normalized.Replace(":", string.Empty).Replace(" ", string.Empty);
        var legacyPrefix = string.Concat("G", "Z");
        var legacyFullName = string.Concat(legacyPrefix, "C", "T", "F");

        if (string.IsNullOrEmpty(normalized) ||
            string.Equals(compactTitle, legacyPrefix, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(compactTitle, legacyFullName, StringComparison.OrdinalIgnoreCase))
            return "YINYU";

        return normalized;
    }

    public static string[] SplitSlogans(string? slogans)
    {
        if (string.IsNullOrWhiteSpace(slogans) ||
            string.Equals(slogans.Trim(), DefaultSlogan, StringComparison.Ordinal))
            return DefaultSlogans;

        var parsed = slogans
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return parsed.Length > 0 ? parsed : DefaultSlogans;
    }

    public static string JoinSlogans(IEnumerable<string?> slogans)
    {
        var parsed = slogans
            .Select(s => s?.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return string.Join('\n', parsed.Length > 0 ? parsed : DefaultSlogans);
    }
}

/// <summary>
/// Client configuration
/// </summary>
[MemoryPackable]
public partial class ClientConfig
{
    /// <summary>
    /// Platform prefix name
    /// </summary>
    public string Title { get; set; } = "YINYU";

    /// <summary>
    /// Platform slogan
    /// </summary>
    public string Slogan { get; set; } = GlobalConfig.JoinSlogans(GlobalConfig.DefaultSlogans);

    /// <summary>
    /// Site description information
    /// </summary>
    public string? Description { get; set; } = GlobalConfig.DefaultDescription;

    /// <summary>
    /// Footer information
    /// </summary>
    public string? FooterInfo { get; set; }

    /// <summary>
    /// Custom theme color
    /// </summary>
    public string? CustomTheme { get; set; }

    /// <summary>
    /// The public key used for API requests
    /// </summary>
    public string? ApiPublicKey { get; set; }

    /// <summary>
    /// Platform logo URL
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Container port mapping type
    /// </summary>
    public ContainerPortMappingType PortMapping { get; set; } = ContainerPortMappingType.Default;

    /// <summary>
    /// Default container lifetime in minutes
    /// </summary>
    public int DefaultLifetime { get; set; } = 120;

    /// <summary>
    /// Extension duration for each renewal in minutes
    /// </summary>
    public int ExtensionDuration { get; set; } = 120;

    /// <summary>
    /// Renewal window before container stops in minutes
    /// </summary>
    public int RenewalWindow { get; set; } = 10;

    [JsonIgnore]
    public DateTimeOffset UpdateTimeUtc { get; set; } = DateTimeOffset.UtcNow;

    public static ClientConfig FromServiceProvider(IServiceProvider serviceProvider) =>
        FromConfigs(
            serviceProvider.GetRequiredService<IOptionsSnapshot<GlobalConfig>>().Value,
            serviceProvider.GetRequiredService<IOptionsSnapshot<ContainerPolicy>>().Value,
            serviceProvider.GetRequiredService<IOptionsSnapshot<ContainerProvider>>().Value,
            serviceProvider.GetRequiredService<IOptionsSnapshot<ManagedConfig>>().Value);

    private static ClientConfig FromConfigs(GlobalConfig globalConfig, ContainerPolicy containerPolicy,
        ContainerProvider containerProvider, ManagedConfig managedConfig) =>
        new()
        {
            Title = globalConfig.Title,
            Slogan = globalConfig.Slogan,
            Description = globalConfig.Description,
            FooterInfo = globalConfig.FooterInfo,
            CustomTheme = globalConfig.CustomTheme,
            LogoUrl = globalConfig.LogoUrl,
            ApiPublicKey = globalConfig.ApiEncryption ? managedConfig.ApiEncryption.PublicKey : null,
            PortMapping = containerProvider.PortMappingType,
            DefaultLifetime = containerPolicy.DefaultLifetime,
            ExtensionDuration = containerPolicy.ExtensionDuration,
            RenewalWindow = containerPolicy.RenewalWindow
        };
}

#region Mail Config

public class SmtpConfig
{
    public string? Host { get; set; } = "127.0.0.1";
    public int? Port { get; set; } = 587;
    public bool BypassCertVerify { get; set; }
}

public class EmailConfig
{
    public string? UserName { get; set; } = string.Empty;
    public string? Password { get; set; } = string.Empty;
    public string? SenderAddress { get; set; } = string.Empty;
    public string? SenderName { get; set; } = string.Empty;
    public SmtpConfig? Smtp { get; set; } = new();
}

#endregion

#region Container Provider

[JsonConverter(typeof(JsonStringEnumConverter<ContainerProviderType>))]
public enum ContainerProviderType
{
    Docker,
    Kubernetes
}

[JsonConverter(typeof(JsonStringEnumConverter<ContainerPortMappingType>))]
public enum ContainerPortMappingType
{
    /// Use default to map the container port to a random port on the host
    Default,

    /// Use platform proxy to map the container tcp to wss
    PlatformProxy
}

public class ContainerProvider
{
    public ContainerProviderType Type { get; set; } = ContainerProviderType.Docker;
    public ContainerPortMappingType PortMappingType { get; set; } = ContainerPortMappingType.Default;
    public bool EnableTrafficCapture { get; set; }
    public string PublicEntry { get; set; } = string.Empty;
    public string? LocalHostAddress { get; set; }
    public KubernetesConfig? KubernetesConfig { get; set; }
    public DockerConfig? DockerConfig { get; set; }
    public NginxProxyConfig? NginxProxyConfig { get; set; }
}

public class DockerConfig
{
    public string Uri { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? ChallengeNetwork { get; set; }
    public int? PublicPortStart { get; set; }
    public int? PublicPortEnd { get; set; }
}

/// <summary>
/// Nginx 反向代理配置，用于分布式调度下统一暴露容器端口
/// </summary>
public class NginxProxyConfig
{
    /// <summary>
    /// 是否启用 Nginx 反向代理模式（远程容器走 Nginx 端口转发）
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    /// 是否由当前 GZCTF 进程写入并重载本机 Nginx stream 配置。
    /// 公网网关独立部署时应设为 false，由网关通过内网接口拉取映射。
    /// </summary>
    public bool SyncLocalConfig { get; set; } = true;

    /// <summary>
    /// Nginx stream 动态配置文件路径
    /// </summary>
    public string ConfigPath { get; set; } = "/etc/nginx/stream-conf.d/gzctf-stream-dynamic.conf";

    /// <summary>
    /// 端口映射同步间隔（秒）
    /// </summary>
    public int SyncIntervalSeconds { get; set; } = 15;

    /// <summary>
    /// Nginx 二进制路径
    /// </summary>
    public string NginxBinaryPath { get; set; } = "nginx";

    /// <summary>
    /// Nginx stream 监听端口段起始
    /// </summary>
    public int ListenPortStart { get; set; } = 30000;

    /// <summary>
    /// Nginx stream 监听端口段结束
    /// </summary>
    public int ListenPortEnd { get; set; } = 30999;

    /// <summary>
    /// 是否在动态配置文件中写入完整 stream {} 块。
    /// 默认 false：生成可被 nginx.conf 的 stream {} include 的片段。
    /// </summary>
    public bool WriteStreamBlock { get; set; }

    /// <summary>
    /// Optional bearer token used by an external public Nginx gateway to pull
    /// the internal port-map endpoint without depending on a user session.
    /// </summary>
    public string? SyncToken { get; set; }
}

public class TeamLabNetworkConfig
{
    /// <summary>
    /// Enables WorkerNode OS network mutation for TeamLab data plane.
    /// Keep disabled unless an isolated WorkerNode has passed network checks.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    /// Execution model for TeamLab runtime deployments. V2 is the platform default; V1 is an
    /// explicit migration mode and is never selected automatically.
    /// </summary>
    public TeamLabExecutionModel ExecutionModel { get; set; } = TeamLabExecutionModel.V2;

    /// <summary>
    /// Returns command plans without mutating WorkerNode state when true.
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// When set, topology address pools must sit entirely inside this range. Empty by default: the
    /// value was never consumed before, so existing deployments have pools spread across RFC1918 and
    /// turning it on without migrating them would make every saved topology fail validation. Set it
    /// once the deployment's topologies have been moved into the range.
    /// </summary>
    public string RuntimeNetworkBaseCidr { get; set; } = string.Empty;

    public string FabricLinkPool { get; set; } = "100.64.0.0/16";

    /// <summary>
    /// Address ranges topologies may not claim, in addition to the built-in ones. Runtime CIDRs are
    /// installed in the WorkerNode host routing table, so anything the node itself routes must be
    /// listed here: node management LANs, extra Docker address pools, storage and database networks.
    /// Site-specific by nature, which is why it has no useful default.
    /// </summary>
    public string[] ReservedCidrs { get; set; } = [];
    public int TeamSubnetPrefixLength { get; set; } = 24;
    public int PublicUdpPortStart { get; set; } = 32000;
    public int PublicUdpPortEnd { get; set; } = 32999;
    public int WorkerWireGuardPortStart { get; set; } = 42000;
    public int WorkerWireGuardPortEnd { get; set; } = 42999;
    public int ManagedDhcpLeaseSeconds { get; set; } = 3600;
    public string BridgePrefix { get; set; } = "tl";
    public string RouterNamespacePrefix { get; set; } = "tlr";
    public string WireGuardInterfacePrefix { get; set; } = "tlwg";
    public int ManagedVmNetworkReadyTimeoutSeconds { get; set; } = 600;
    public int ManagedVmObservationReadyTimeoutSeconds { get; set; } = 300;
    public int ManagedVmBootstrapOverheadSeconds { get; set; } = 300;
    public int ManagedVmRebootAllowanceSeconds { get; set; } = 300;
    public int ManagedVmMaximumBootstrapTimeoutSeconds { get; set; } = 28800;
    public int RecoveryGraceSeconds { get; set; } = 30;
}

public class PublicUdpGatewayConfig
{
    /// <summary>
    /// Enables public UDP gateway rule mutation.
    /// </summary>
    public bool Enable { get; set; }

    public string Provider { get; set; } = "nftables";
    public string PublicEndpoint { get; set; } = string.Empty;
    public string NftTable { get; set; } = "inet gzctf_teamlab";
    public string IptablesBinaryPath { get; set; } = "iptables";
    public string NftBinaryPath { get; set; } = "nft";
}

public sealed record TeamLabUdpMappingEntry(
    int PublicUdpPort,
    string WorkerTunnelIp,
    int WorkerWireGuardPort,
    Guid RuntimeId,
    int RuleVersion,
    bool IsSynced,
    string? LastSyncError);

public class KubernetesConfig
{
    public string Namespace { get; set; } = "gzctf-challenges";
    public string KubeConfig { get; set; } = "kube-config.yaml";
    public string[]? AllowCidr { get; set; }
    public string[]? Dns { get; set; }
}

public class RegistrySet<T> : Dictionary<string, T>
    where T : class
{
    public T? GetForImage(string image)
    {
        if (string.IsNullOrWhiteSpace(image))
            return null;

        image = image.Contains("://") ? image : $"https://{image}";

        if (!Uri.TryCreate(image, UriKind.Absolute, out var uri) || uri.HostNameType == UriHostNameType.Unknown)
            return null;

        return TryGetValue(uri.Authority, out var cfg) ? cfg :
            TryGetValue(uri.Host, out var cfgHost) ? cfgHost : null;
    }
}

public class RegistryConfig
{
    public string? ServerAddress { get; set; }
    public string? UserName { get; set; }
    public string? Password { get; set; }

    public bool Valid => !string.IsNullOrEmpty(UserName) &&
                         !string.IsNullOrEmpty(Password);
}

#endregion

#region Captcha Provider

[JsonConverter(typeof(JsonStringEnumConverter<CaptchaProvider>))]
public enum CaptchaProvider
{
    None,
    HashPow,
    CloudflareTurnstile
}

public class HashPowConfig
{
    // How many leading zeros the hash should have
    private int _difficulty = 18;

    public int Difficulty
    {
        set => _difficulty = value;
        get => _difficulty = Math.Clamp(_difficulty, 8, 48);
    }
}

public class CaptchaConfig
{
    public CaptchaProvider Provider { get; set; }
    public string? SecretKey { get; set; }
    public string? SiteKey { get; set; }
    public HashPowConfig HashPow { get; set; } = new();
}

#endregion

#region Telemetry

public class TelemetryConfig
{
    public PrometheusConfig Prometheus { get; set; } = new();
    public OpenTelemetryConfig OpenTelemetry { get; set; } = new();
    public AzureMonitorConfig AzureMonitor { get; set; } = new();
    public ConsoleConfig Console { get; set; } = new();

    [JsonIgnore]
    public bool Enable => Prometheus.Enable || OpenTelemetry.Enable || AzureMonitor.Enable || Console.Enable;
}

public class PrometheusConfig
{
    public bool Enable { get; set; }
    public bool TotalNameSuffixForCounters { get; set; }
}

public class OpenTelemetryConfig
{
    public bool Enable { get; set; }
    public OtlpExportProtocol Protocol { get; set; }
    public string? EndpointUri { get; set; }
}

public class AzureMonitorConfig
{
    public bool Enable { get; set; }
    public string? ConnectionString { get; set; }
}

public class ConsoleConfig
{
    public bool Enable { get; set; }
}

#endregion

public class GrafanaLokiOptions
{
    public bool Enable { get; set; }
    public string? EndpointUri { get; set; }
    public LokiLabel[]? Labels { get; set; }
    public string[]? PropertiesAsLabels { get; set; }
    public LokiCredentials? Credentials { get; set; }
    public string? Tenant { get; set; }
    public LogLevel? MinimumLevel { get; set; }
}

public class ForwardedOptions : ForwardedHeadersOptions
{
    // For historical configuration compatibility as we accept string
    public new List<string>? KnownIPNetworks { get; set; }
    public new List<string>? KnownProxies { get; set; }

    // Old properties for compatibility
    public new List<string>? KnownNetworks { get; set; }
    public List<string>? TrustedNetworks { get; set; }
    public List<string>? TrustedProxies { get; set; }

    public void ToForwardedHeadersOptions(ForwardedHeadersOptions options)
    {
        // assign the same value to the base class via reflection
        var type = typeof(ForwardedHeadersOptions);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var property in properties)
        {
            // skip the properties that are not being set directly
            // .NET 10 update: `KnownNetworks` is obsolete, needs to be skipped
            if (property.Name is nameof(KnownIPNetworks) or nameof(KnownProxies) or "KnownNetworks")
                continue;

            property.SetValue(options, property.GetValue(this));
        }

        // Handle KnownIPNetworks
        Action<string> addNetwork = networkString =>
        {
            // split the network into address and prefix length
            var parts = networkString.Split('/');
            if (parts.Length == 2 &&
                IPAddress.TryParse(parts[0], out var prefix) &&
                int.TryParse(parts[1], out var prefixLength))
                options.KnownIPNetworks.Add(new IPNetwork(prefix, prefixLength));
        };

        KnownIPNetworks?.ForEach(addNetwork);
        KnownNetworks?.ForEach(addNetwork);
        TrustedNetworks?.ForEach(addNetwork);

        // Handle KnownProxies
        Action<string> addProxies = proxy =>
            Array.ForEach(proxy.ResolveIP(), ip => options.KnownProxies.Add(ip));

        KnownProxies?.ForEach(addProxies);
        TrustedProxies?.ForEach(addProxies);
    }
}
