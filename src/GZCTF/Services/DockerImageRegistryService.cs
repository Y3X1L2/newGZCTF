using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Services.Fleet;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GZCTF.Services;

public sealed record DockerImageUploadResult(
    string FullImage,
    string SourceImage,
    string? ImageId,
    string? Digest = null);

public class DockerImageRegistryService
{
    public const string InternalReferencePrefix = "gzctf-internal://";
    private static readonly TimeSpan DockerCommandTimeout = TimeSpan.FromMinutes(30);
    private static readonly SemaphoreSlim LocalRegistryTrustLock = new(1, 1);

    static readonly Regex RepositoryRegex = new("^[a-z0-9]+(?:[._/-][a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static readonly Regex TagRegex = new("^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    readonly DockerRegistrySettings _settings;
    readonly IServiceScopeFactory _scopeFactory;
    readonly AgentClient _agentClient;
    readonly ILogger<DockerImageRegistryService> _logger;
    readonly IHttpClientFactory? _httpClientFactory;

    public DockerImageRegistryService(IOptions<DockerRegistrySettings> options,
        IServiceScopeFactory scopeFactory,
        AgentClient agentClient,
        ILogger<DockerImageRegistryService> logger,
        IHttpClientFactory? httpClientFactory = null)
    {
        _settings = options.Value;
        _scopeFactory = scopeFactory;
        _agentClient = agentClient;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public long MaxUploadSizeBytes => _settings.MaxUploadSizeBytes;

    public string RegistryAddress => _settings.NormalizedAddress;

    public string RegistryNamespace => _settings.NormalizedNamespace;

    public int MaxUploadSizeGb => _settings.MaxUploadSizeGb;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(RegistryAddress);

    public async Task<bool> IsConfiguredAsync(CancellationToken token = default) =>
        await GetActiveEndpointAsync(token) is not null;

    public async Task<DockerRegistryEndpoint?> GetActiveEndpointAsync(CancellationToken token = default)
    {
        await Task.CompletedTask;
        var address = _settings.NormalizedAddress;
        return string.IsNullOrWhiteSpace(address)
            ? null
            : new DockerRegistryEndpoint(null, "Fixed Registry", address, null, address,
                _settings.NormalizedNamespace, false);
    }

    public async Task<string> GetRegistryAddressAsync(CancellationToken token = default) =>
        (await GetActiveEndpointAsync(token))?.Address ?? string.Empty;

    public async Task<IReadOnlyCollection<string>> GetManagedRegistryAddressesAsync(CancellationToken token = default)
    {
        var registries = await BuildFleetRegistryTrustCandidatesAsync(token);
        return registries.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<DockerRegistryEndpoint> EnsureActiveRegistryAsync(CancellationToken token = default)
    {
        var endpoint = await GetActiveEndpointAsync(token)
                       ?? throw new InvalidOperationException("Internal Docker registry address is not configured.");
        return endpoint;
    }

    public async Task RepairLegacyLocalRegistryImageReferencesAsync(CancellationToken token = default)
    {
        var endpoint = await GetActiveEndpointAsync(token);
        if (endpoint is null || string.IsNullOrWhiteSpace(endpoint.Address))
            return;

        var legacyRegistries = await BuildManagedRegistryCandidatesAsync(endpoint, token);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var templates = await context.ImageTemplates
            .Where(t => t.ImageType == ImageType.Docker && !string.IsNullOrWhiteSpace(t.RegistryUrl))
            .ToArrayAsync(token);

        var changed = 0;
        foreach (var template in templates)
        {
            var updated = TryConvertManagedImageToInternalReference(template.RegistryUrl!, legacyRegistries);
            if (updated == template.RegistryUrl)
                continue;

            template.RegistryUrl = updated;
            changed++;
        }

        if (changed <= 0)
            return;

        await context.SaveChangesAsync(token);
        _logger.LogInformation("Converted {Count} legacy local Docker image registry reference(s) to dynamic internal references.",
            changed);
    }

    public async Task EnsureNodeRegistryAsync(Guid nodeId, CancellationToken token = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var node = await context.WorkerNodes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == nodeId, token)
                   ?? throw new InvalidOperationException("镜像存储节点不存在。");

        if (node.IsLocal)
        {
            await EnsureLocalRegistryAsync(node.RegistryPort, token);
            return;
        }

        await _agentClient.EnsureDockerRegistryAsync(node.Id, node.RegistryPort, token);
    }

    public async Task ConfigureNodeRegistryTrustAsync(Guid nodeId, string registryAddress,
        CancellationToken token = default) =>
        await ConfigureNodeRegistryTrustAsync(nodeId, [registryAddress], token);

    public async Task ConfigureNodeRegistryTrustAsync(Guid nodeId, IReadOnlyCollection<string> registryAddresses,
        CancellationToken token = default)
    {
        var normalized = NormalizeRegistryAddresses(registryAddresses);
        if (normalized.Length == 0)
            return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var node = await context.WorkerNodes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == nodeId, token);
        if (node is null)
            return;

        if (node.IsLocal)
        {
            await ConfigureLocalInsecureRegistriesAsync(normalized, token);
            return;
        }

        await _agentClient.ConfigureDockerRegistriesAsync(node.Id, normalized, token);
    }

    public async Task ConfigureFleetRegistryTrustAsync(string registryAddress, CancellationToken token = default)
    {
        await ConfigureFleetRegistryTrustAsync([registryAddress], token);
    }

    public async Task ConfigureManagedRegistryTrustAsync(CancellationToken token = default)
    {
        var registries = await GetManagedRegistryAddressesAsync(token);
        await ConfigureFleetRegistryTrustAsync(registries, token);
    }

    public async Task ConfigureFleetRegistryTrustAsync(IReadOnlyCollection<string> registryAddresses,
        CancellationToken token = default)
    {
        var normalized = NormalizeRegistryAddresses(registryAddresses);
        if (normalized.Length == 0)
            return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var nodes = await context.WorkerNodes.AsNoTracking()
            .Where(n => (n.Capabilities & NodeCapability.Docker) == NodeCapability.Docker)
            .OrderByDescending(n => n.IsLocal)
            .ThenBy(n => n.Name)
            .ToArrayAsync(token);

        foreach (var node in nodes)
        {
            try
            {
                await ConfigureNodeRegistryTrustAsync(node.Id, normalized, token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to configure Docker registry trust on node {NodeName} ({NodeId}) for {Registries}.",
                    node.Name, node.Id, string.Join(", ", normalized));
            }
        }
    }

    static string[] NormalizeRegistryAddresses(IEnumerable<string?> registryAddresses) =>
        registryAddresses
            .Select(NormalizeRegistryAddress)
            .Where(r => !string.IsNullOrWhiteSpace(r) &&
                        !r.StartsWith(InternalReferencePrefix, StringComparison.OrdinalIgnoreCase) &&
                        LooksLikeRegistryHost(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public string BuildInternalImageReference(string repository, string tag)
    {
        var path = BuildInternalImagePath(repository, tag);
        return $"{InternalReferencePrefix}{path}";
    }

    public async Task<string> BuildInternalImageReferenceAsync(string repository, string tag,
        CancellationToken token = default) => await Task.FromResult(BuildInternalImageReference(repository, tag));

    public async Task<DockerImageUploadResult> ImportArchiveAsync(string archivePath, string repository, string tag,
        string? sourceImage, CancellationToken token)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("Docker image archive was not found.", archivePath);

        var endpoint = await EnsureActiveRegistryAsync(token);
        var storedImage = BuildInternalImageReference(repository, tag);
        var targetImage = BuildImageReferenceForRegistry(endpoint.Address, repository, tag);
        var load = await RunDockerAsync(["load", "-i", archivePath], token);
        var loadedImage = string.IsNullOrWhiteSpace(sourceImage)
            ? ParseLoadedImageReference(load.Output)
            : sourceImage.Trim();

        if (string.IsNullOrWhiteSpace(loadedImage))
            throw new InvalidOperationException("Cannot resolve loaded image name. Please provide source image.");

        await RunDockerAsync(["tag", loadedImage, targetImage], token);
        await RunDockerAsync(["push", targetImage], token);

        var (imageId, digest) = await InspectImageAsync(targetImage, token);
        return new DockerImageUploadResult(storedImage, loadedImage, imageId, digest);
    }

    public async Task<DockerImageUploadResult> ImportReferenceAsync(
        string sourceImage,
        string repository,
        string tag,
        CancellationToken token)
    {
        var source = sourceImage.Trim();
        if (string.IsNullOrWhiteSpace(source) || source.Any(char.IsWhiteSpace))
            throw new InvalidOperationException("Docker image reference is invalid.");

        var endpoint = await EnsureActiveRegistryAsync(token);
        var storedImage = BuildInternalImageReference(repository, tag);
        var targetImage = BuildImageReferenceForRegistry(endpoint.Address, repository, tag);
        await RunDockerAsync(["pull", source], token);
        await RunDockerAsync(["tag", source, targetImage], token);
        await RunDockerAsync(["push", targetImage], token);

        var (imageId, digest) = await InspectImageAsync(targetImage, token);
        return new DockerImageUploadResult(storedImage, source, imageId, digest);
    }

    async Task<(string? ImageId, string? Digest)> InspectImageAsync(
        string image,
        CancellationToken token)
    {
        string? imageId = null;
        string? digest = null;
        try
        {
            var inspect = await RunDockerAsync(
                ["image", "inspect", image, "--format", "{{.Id}}"], token);
            imageId = inspect.Output.Trim().Split(
                '\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();

            var repoDigests = await RunDockerAsync(
                ["image", "inspect", image, "--format", "{{json .RepoDigests}}"], token);
            var references = JsonSerializer.Deserialize<string[]>(repoDigests.Output.Trim()) ?? [];
            digest = references
                .Select(reference => reference[(reference.LastIndexOf('@') + 1)..])
                .FirstOrDefault(value => value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Failed to inspect imported Docker image {Image}", image);
        }

        return (imageId, digest);
    }

    public async Task<string> ResolveImageTemplateReferenceAsync(string imageName, string? registryOrImage,
        CancellationToken token = default)
    {
        var pullTarget = DockerImageReference.ResolvePullTarget(imageName, registryOrImage);
        return await ResolveImageReferenceAsync(pullTarget.FullImage, token);
    }

    public string ResolveInternalImageReferenceForConfiguredRegistry(string image)
    {
        var normalized = NormalizeRegistryAddress(image);
        if (string.IsNullOrWhiteSpace(normalized))
            return normalized;

        if (!TryGetInternalImagePath(normalized, out var internalPath))
            return normalized;

        var address = RegistryAddress;
        if (string.IsNullOrWhiteSpace(address))
            throw new InvalidOperationException("Internal Docker registry address is not configured.");

        return $"{address}/{internalPath.TrimStart('/')}";
    }

    public async Task<string> ResolveImageReferenceAsync(string image, CancellationToken token = default)
    {
        var normalized = NormalizeRegistryAddress(image);
        if (string.IsNullOrWhiteSpace(normalized))
            return normalized;

        if (TryGetInternalImagePath(normalized, out var internalPath))
            return await BuildImageReferenceForActiveRegistryAsync(internalPath, token);

        var endpoint = await GetActiveEndpointAsync(token);
        var managedRegistries = await BuildManagedRegistryCandidatesAsync(endpoint, token);
        var converted = TryConvertManagedImageToInternalReference(normalized, managedRegistries);
        if (!string.Equals(converted, normalized, StringComparison.Ordinal))
        {
            if (TryGetInternalImagePath(converted, out var convertedPath))
                return await BuildImageReferenceForActiveRegistryAsync(convertedPath, token);
        }

        return normalized;
    }

    public virtual async Task DeleteManagedImageAsync(string? image, CancellationToken token = default)
    {
        if (!TryGetInternalImagePath(NormalizeRegistryAddress(image), out var internalPath))
            return;

        var separator = internalPath.LastIndexOf(':');
        var slash = internalPath.LastIndexOf('/');
        var repository = separator > slash ? internalPath[..separator] : internalPath;
        var reference = separator > slash ? internalPath[(separator + 1)..] : "latest";
        var address = RegistryAddress;
        if (string.IsNullOrWhiteSpace(address))
            throw new InvalidOperationException("Internal Docker registry address is not configured.");

        using var fallbackClient = _httpClientFactory is null ? new HttpClient() : null;
        var client = _httpClientFactory?.CreateClient() ?? fallbackClient!;
        var manifestUrl = $"http://{address}/v2/{repository}/manifests/{Uri.EscapeDataString(reference)}";
        using var head = new HttpRequestMessage(HttpMethod.Head, manifestUrl);
        head.Headers.Accept.ParseAdd("application/vnd.docker.distribution.manifest.v2+json");
        head.Headers.Accept.ParseAdd("application/vnd.oci.image.manifest.v1+json");
        using var headResponse = await client.SendAsync(head, token);
        if (headResponse.StatusCode == HttpStatusCode.NotFound)
            return;
        if (!headResponse.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Failed to resolve Docker manifest {repository}:{reference} from {address}: " +
                $"{(int)headResponse.StatusCode} {headResponse.StatusCode}.");

        if (!headResponse.Headers.TryGetValues("Docker-Content-Digest", out var digestValues))
            throw new InvalidOperationException(
                $"Registry {address} did not return Docker-Content-Digest for {repository}:{reference}.");
        var digest = digestValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(digest))
            throw new InvalidOperationException(
                $"Registry {address} returned an empty manifest digest for {repository}:{reference}.");

        using var deleteResponse = await client.DeleteAsync(
            $"http://{address}/v2/{repository}/manifests/{Uri.EscapeDataString(digest)}",
            token);
        if (!deleteResponse.IsSuccessStatusCode && deleteResponse.StatusCode != HttpStatusCode.NotFound)
            throw new InvalidOperationException(
                $"Failed to delete Docker manifest {repository}@{digest} from {address}: " +
                $"{(int)deleteResponse.StatusCode} {deleteResponse.StatusCode}.");
    }

    public async Task<bool> IsManagedImageReferenceAsync(string? image, CancellationToken token = default)
    {
        var normalized = NormalizeRegistryAddress(image);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (TryGetInternalImagePath(normalized, out _))
            return true;

        var endpoint = await GetActiveEndpointAsync(token);
        var managedRegistries = await BuildManagedRegistryCandidatesAsync(endpoint, token);
        var converted = TryConvertManagedImageToInternalReference(normalized, managedRegistries);
        return !string.Equals(converted, normalized, StringComparison.Ordinal);
    }

    public string ToInternalImageReference(string imageReference)
    {
        var path = ExtractImagePath(imageReference);
        return $"{InternalReferencePrefix}{path}";
    }

    public string BuildImageReferenceForRegistryFromReference(string registryAddress, string imageReference)
    {
        var path = ExtractImagePath(imageReference);
        var address = NormalizeRegistryAddress(registryAddress);
        if (string.IsNullOrWhiteSpace(address))
            throw new InvalidOperationException("Docker registry address is required.");

        return $"{address}/{path}";
    }

    public string BuildImageReferenceForRegistry(string registryAddress, string repository, string tag)
    {
        var address = NormalizeRegistryAddress(registryAddress);
        if (string.IsNullOrWhiteSpace(address))
            throw new InvalidOperationException("Docker registry address is required.");

        return $"{address}/{BuildInternalImagePath(repository, tag)}";
    }

    public async Task<DockerCommandResult> RunDockerCommandAsync(IReadOnlyList<string> arguments,
        CancellationToken token) => await RunDockerAsync(arguments, token);

    public async Task EnsureLocalRegistryAsync(int port, CancellationToken token)
    {
        port = Math.Clamp(port, 1, 65535);
        if (OperatingSystem.IsLinux())
        {
            await RunProcessAsync("bash", ["-lc", BuildEnsureRegistryScript(port)], TimeSpan.FromMinutes(5), token);
            return;
        }

        const string containerName = "gzctf-internal-registry";

        try
        {
            var inspect = await RunDockerAsync(["container", "inspect", containerName, "--format", "{{.State.Running}}"],
                token);
            if (inspect.Output.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
                return;

            await RunDockerAsync(["start", containerName], token);
            return;
        }
        catch (InvalidOperationException)
        {
            // Container does not exist, create a managed registry below.
        }

        try
        {
            await RunDockerAsync(["image", "inspect", "registry:2"], token);
        }
        catch (InvalidOperationException)
        {
            await RunDockerAsync(["pull", "registry:2"], token);
        }

        Directory.CreateDirectory("/var/lib/gzctf-registry");
        await RunDockerAsync([
            "run", "-d",
            "--restart", "always",
            "--name", containerName,
            "-p", $"0.0.0.0:{port}:5000",
            "-v", "/var/lib/gzctf-registry:/var/lib/registry",
            "registry:2"
        ], token);
    }

    static string BuildEnsureRegistryScript(int port) => $$"""
set -euo pipefail
name="gzctf-internal-registry"
port="{{port}}"
store="/var/lib/gzctf-registry"
mkdir -p "$store"

copy_registry_data() {
  src_name="$1"
  mount_source="$(docker inspect -f '{{"{{"}}range .Mounts{{"}}"}}{{"{{"}}if eq .Destination "/var/lib/registry"{{"}}"}}{{"{{"}}.Source{{"}}"}}{{"{{"}}end{{"}}"}}{{"{{"}}end{{"}}"}}' "$src_name" 2>/dev/null || true)"
  if [ -n "$mount_source" ] && [ "$mount_source" != "$store" ] && [ -d "$mount_source" ]; then
    cp -a "$mount_source"/. "$store"/
  elif [ -z "$mount_source" ]; then
    docker cp "$src_name:/var/lib/registry/." "$store"/ 2>/dev/null || true
  fi
}

publishes_registry_port() {
  src_name="$1"
  docker port "$src_name" 5000/tcp 2>/dev/null | awk -v p="$port" '
    $0 ~ ":" p "$" { found=1 }
    END { exit found ? 0 : 1 }
  '
}

has_wildcard_binding() {
  docker port "$name" 5000/tcp 2>/dev/null | awk -v p="$port" '
    $0 ~ ":" p "$" && $0 !~ /^127\./ && $0 !~ /^\[::1\]/ && $0 !~ /^::1/ { found=1 }
    END { exit found ? 0 : 1 }
  '
}

if docker container inspect "$name" >/dev/null 2>&1; then
  if ! docker inspect -f '{{"{{"}}.State.Running{{"}}"}}' "$name" 2>/dev/null | grep -qi true; then
    docker start "$name" >/dev/null || true
  fi

  if has_wildcard_binding; then
    exit 0
  fi

  copy_registry_data "$name"
  docker rm -f "$name" >/dev/null
fi

# Older builds and manual deployments may have created another local registry container
# bound only to 127.0.0.1:${port}. Migrate its registry data before freeing the port.
for old_name in $(docker ps -a --filter ancestor=registry:2 --format '{{"{{"}}.Names{{"}}"}}' 2>/dev/null || true); do
  if [ "$old_name" = "$name" ]; then
    continue
  fi

  if publishes_registry_port "$old_name"; then
    copy_registry_data "$old_name"
    docker rm -f "$old_name" >/dev/null
  fi
done

if ! docker image inspect registry:2 >/dev/null 2>&1; then
  timeout 180 docker pull registry:2 >/dev/null
fi

docker run -d \
  --restart always \
  --name "$name" \
  -p "0.0.0.0:${port}:5000" \
  -v "$store:/var/lib/registry" \
  registry:2 >/dev/null
""";

    public async Task ConfigureLocalInsecureRegistryAsync(string registryAddress, CancellationToken token)
    {
        await ConfigureLocalInsecureRegistriesAsync([registryAddress], token);
    }

    public async Task ConfigureLocalInsecureRegistriesAsync(IReadOnlyCollection<string> registryAddresses,
        CancellationToken token)
    {
        if (!OperatingSystem.IsLinux())
            return;

        var normalized = NormalizeRegistryAddresses(registryAddresses);
        if (normalized.Length == 0)
            return;

        await LocalRegistryTrustLock.WaitAsync(token);
        try
        {
            Directory.CreateDirectory("/etc/docker");
            var quotedRegistries = string.Join(' ', normalized.Select(ShellQuote));
            var script = $$"""
set -euo pipefail
registries=({{quotedRegistries}})
path="/etc/docker/daemon.json"
tmp="$(mktemp /tmp/gzctf-daemon.XXXXXX.json)"
trap 'rm -f "$tmp"' EXIT
python3 - "${registries[@]}" <<'PY' > "$tmp"
import json, os, sys
path="/etc/docker/daemon.json"
requested=[r.strip() for r in sys.argv[1:] if r.strip()]
data={}
try:
    if os.path.exists(path) and os.path.getsize(path)>0:
        with open(path, "r", encoding="utf-8") as f:
            data=json.load(f)
except Exception:
    data={}
registries=data.get("insecure-registries")
if not isinstance(registries, list):
    registries=[]
for registry in requested:
    if registry not in registries:
        registries.append(registry)
data["insecure-registries"]=registries
print(json.dumps(data, ensure_ascii=False, indent=2))
PY
if [ -f "$path" ] && cmp -s "$tmp" "$path"; then
  exit 0
fi
if [ -f "$path" ]; then cp -a "$path" "$path.bak.$(date +%Y%m%d%H%M%S)" || true; fi
install -m 0644 "$tmp" "$path"
if command -v systemctl >/dev/null 2>&1; then
  systemctl restart docker
else
  service docker restart
fi
""";
            await RunProcessAsync("bash", ["-lc", script], TimeSpan.FromSeconds(90), token);
        }
        finally
        {
            LocalRegistryTrustLock.Release();
        }
    }

    public static string NormalizeRegistryAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().TrimEnd('/');
        if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["http://".Length..];
        else if (normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["https://".Length..];

        return normalized;
    }

    static string NormalizeHost(string? value)
    {
        var normalized = NormalizeRegistryAddress(value);
        var colon = normalized.LastIndexOf(':');
        return colon > 0 && int.TryParse(normalized[(colon + 1)..], out _)
            ? normalized[..colon]
            : normalized;
    }

    static string NormalizeRepository(string repository)
    {
        var value = repository.Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Docker repository is required.");

        value = value.ToLowerInvariant();
        if (value.Contains("..", StringComparison.Ordinal) || value.Contains(':', StringComparison.Ordinal) ||
            !RepositoryRegex.IsMatch(value))
            throw new InvalidOperationException("Docker repository can only contain lowercase letters, numbers, '.', '_', '-' and '/'.");

        return value;
    }

    string BuildInternalImagePath(string repository, string tag)
    {
        var normalizedRepository = NormalizeRepository(repository);
        var normalizedTag = NormalizeTag(tag);
        var ns = _settings.NormalizedNamespace;

        var path = string.IsNullOrWhiteSpace(ns)
            ? normalizedRepository
            : $"{ns}/{normalizedRepository}";

        return $"{path}:{normalizedTag}";
    }

    async Task<string> BuildImageReferenceForActiveRegistryAsync(string imagePath, CancellationToken token)
    {
        var endpoint = await EnsureActiveRegistryAsync(token);
        return $"{endpoint.Address}/{imagePath.TrimStart('/')}";
    }

    async Task<HashSet<string>> BuildManagedRegistryCandidatesAsync(DockerRegistryEndpoint? endpoint,
        CancellationToken token)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ports = new HashSet<int> { endpoint?.Port ?? 5000, 5000 };

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var nodes = await context.WorkerNodes.AsNoTracking()
            .Select(n => new { n.HostAddress, n.RegistryPort })
            .ToArrayAsync(token);
        var migrations = await context.DockerRegistryMigrationTasks.AsNoTracking()
            .Select(t => new { t.SourceRegistry, t.TargetRegistry })
            .ToArrayAsync(token);
        var migrationItems = await context.DockerRegistryMigrationItems.AsNoTracking()
            .Select(i => new { i.SourceImage, i.TargetImage })
            .ToArrayAsync(token);

        foreach (var node in nodes)
        {
            ports.Add(node.RegistryPort);
            var host = NormalizeHost(node.HostAddress);
            if (!string.IsNullOrWhiteSpace(host))
                candidates.Add(NormalizeRegistryAddress($"{host}:{node.RegistryPort}"));
        }

        if (endpoint is not null)
            candidates.Add(NormalizeRegistryAddress(endpoint.Address));

        foreach (var port in ports)
        {
            foreach (var host in new[] { "localhost", "127.0.0.1", "0.0.0.0", "[::1]", "::1" })
                candidates.Add(NormalizeRegistryAddress($"{host}:{port}"));
        }

        var configured = NormalizeRegistryAddress(_settings.NormalizedAddress);
        if (!string.IsNullOrWhiteSpace(configured))
            candidates.Add(configured);

        var fixedRegistry = NormalizeRegistryAddress(DockerRegistrySettings.FixedAddress);
        if (!string.IsNullOrWhiteSpace(fixedRegistry))
            candidates.Add(fixedRegistry);

        foreach (var migration in migrations)
        {
            AddRegistryCandidate(candidates, migration.SourceRegistry);
            AddRegistryCandidate(candidates, migration.TargetRegistry);
        }

        foreach (var item in migrationItems)
        {
            AddRegistryCandidateFromImage(candidates, item.SourceImage);
            AddRegistryCandidateFromImage(candidates, item.TargetImage);
        }

        return candidates;
    }

    async Task<HashSet<string>> BuildFleetRegistryTrustCandidatesAsync(CancellationToken token)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var endpoint = await GetActiveEndpointAsync(token);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (endpoint is not null)
            candidates.Add(NormalizeRegistryAddress(endpoint.Address));

        var configured = NormalizeRegistryAddress(_settings.NormalizedAddress);
        if (!string.IsNullOrWhiteSpace(configured))
            candidates.Add(configured);

        return candidates;
    }

    static void AddRegistryCandidate(HashSet<string> candidates, string? registry)
    {
        var normalized = NormalizeRegistryAddress(registry);
        if (!string.IsNullOrWhiteSpace(normalized) &&
            !normalized.StartsWith(InternalReferencePrefix, StringComparison.OrdinalIgnoreCase))
            candidates.Add(normalized);
    }

    static void AddRegistryCandidateFromImage(HashSet<string> candidates, string? image)
    {
        var normalized = NormalizeRegistryAddress(image);
        if (normalized.StartsWith(InternalReferencePrefix, StringComparison.OrdinalIgnoreCase))
            return;

        var slash = normalized.IndexOf('/');
        if (slash <= 0)
            return;

        var registry = normalized[..slash];
        if (LooksLikeRegistryHost(registry))
            candidates.Add(registry);
    }

    static string TryConvertManagedImageToInternalReference(string image, HashSet<string> managedRegistries)
    {
        var normalized = NormalizeRegistryAddress(image);
        if (TryGetInternalImagePath(normalized, out _))
            return normalized;

        var slash = normalized.IndexOf('/');
        if (slash <= 0)
            return image;

        var registry = normalized[..slash];
        if (!managedRegistries.Contains(registry))
            return image;

        return $"{InternalReferencePrefix}{normalized[(slash + 1)..]}";
    }

    static bool TryGetInternalImagePath(string image, out string path)
    {
        path = string.Empty;
        if (!image.StartsWith(InternalReferencePrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        path = image[InternalReferencePrefix.Length..].TrimStart('/');
        return !string.IsNullOrWhiteSpace(path);
    }

    static string ExtractImagePath(string imageReference)
    {
        var image = NormalizeRegistryAddress(imageReference);
        if (TryGetInternalImagePath(image, out var internalPath))
            return internalPath;

        var slash = image.IndexOf('/');
        if (slash > 0 && LooksLikeRegistryHost(image[..slash]))
            return image[(slash + 1)..];

        return image;
    }

    static bool LooksLikeRegistryHost(string value)
    {
        if (value.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        if (value.Contains('.') || value.Contains(':'))
            return true;

        return false;
    }

    static string NormalizeTag(string tag)
    {
        var value = string.IsNullOrWhiteSpace(tag) ? "latest" : tag.Trim();
        if (!TagRegex.IsMatch(value))
            throw new InvalidOperationException("Docker tag can only contain letters, numbers, '.', '_' and '-'.");

        return value;
    }

    static string? ParseLoadedImageReference(string output)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Reverse())
        {
            const string imagePrefix = "Loaded image:";
            const string imageIdPrefix = "Loaded image ID:";
            if (line.StartsWith(imagePrefix, StringComparison.OrdinalIgnoreCase))
                return line[imagePrefix.Length..].Trim();
            if (line.StartsWith(imageIdPrefix, StringComparison.OrdinalIgnoreCase))
                return line[imageIdPrefix.Length..].Trim();
        }

        return null;
    }

    async Task<DockerCommandResult> RunDockerAsync(IReadOnlyList<string> arguments, CancellationToken token)
    {
        _logger.LogInformation("Running docker {Arguments}", string.Join(' ', arguments));
        var result = await ProcessExecution.RunAsync(
            "docker",
            arguments.Select(NormalizeShellArgument).ToArray(),
            DockerCommandTimeout,
            token);

        if (result.ExitCode != 0)
        {
            _logger.LogWarning("docker {Arguments} failed with exit code {ExitCode}: {Error}",
                string.Join(' ', arguments), result.ExitCode, result.Error.Trim());
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error)
                ? $"docker command failed with exit code {result.ExitCode}"
                : result.Error.Trim());
        }

        return new DockerCommandResult(result.Output, result.Error);
    }

    static async Task RunProcessAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout,
        CancellationToken token)
    {
        var result = await ProcessExecution.RunAsync(
            fileName,
            arguments.Select(NormalizeShellArgument).ToArray(),
            timeout,
            token);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error)
                ? $"{fileName} exited with code {result.ExitCode}: {result.Output.Trim()}"
                : result.Error.Trim());
    }

    static string NormalizeShellArgument(string argument) => argument.Replace("\r\n", "\n").Replace("\r", "\n");

    static string ShellQuote(string value) => $"'{value.Replace("'", "'\"'\"'")}'";

    public readonly record struct DockerCommandResult(string Output, string Error);
}

public sealed record DockerRegistryEndpoint(
    Guid? NodeId,
    string NodeName,
    string Host,
    int? Port,
    string Address,
    string Namespace,
    bool IsLocal);
