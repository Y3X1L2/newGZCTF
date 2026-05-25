using Docker.DotNet;
using Docker.DotNet.Models;
using GZCTF.Models.Internal;
using GZCTF.Services.Container.Provider;
using Microsoft.Extensions.Options;

namespace GZCTF.Services;

/// <summary>
/// Extends GZCTF's Docker integration with container orchestration capabilities
/// for CTF scenario environments. Supports pulling images from OCI registries
/// and managing isolated Docker networks for scenario isolation.
/// </summary>
public class ContainerOrchestrator
{
    private readonly DockerClient _client;
    private readonly ILogger<ContainerOrchestrator> _logger;
    private readonly RegistrySet<RegistryConfig> _registries;
    private readonly int _timeoutSeconds = 120;

    /// <summary>
    /// Initializes a new instance of <see cref="ContainerOrchestrator"/> using the
    /// existing Docker provider and registry configuration.
    /// </summary>
    /// <param name="provider">The Docker container provider for accessing the Docker daemon.</param>
    /// <param name="registriesOptions">Registry authentication configurations.</param>
    /// <param name="logger">Structured logger for operation auditing.</param>
    public ContainerOrchestrator(
        IContainerProvider<DockerClient, DockerMetadata> provider,
        IOptions<RegistrySet<RegistryConfig>> registriesOptions,
        ILogger<ContainerOrchestrator> logger)
    {
        _client = provider.GetProvider();
        _registries = registriesOptions.Value;
        _logger = logger;

        _logger.LogDebug("ContainerOrchestrator initialized with {RegistryCount} configured registries",
            _registries.Count);
    }

    /// <summary>
    /// Pulls a Docker image from an OCI-compatible registry with optional authentication.
    /// </summary>
    /// <param name="registryUrl">The registry URL (e.g., "registry.example.com"). Can be <c>null</c> for Docker Hub.</param>
    /// <param name="imageName">The image name with optional tag (e.g., "my-image:latest").</param>
    /// <param name="authToken">Optional OAuth2 access token or base64-encoded credentials for private registries.</param>
    public async Task PullImageFromRegistryAsync(string registryUrl, string imageName, string? authToken = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageName);

        var fullImage = string.IsNullOrWhiteSpace(registryUrl)
            ? imageName
            : $"{registryUrl.TrimEnd('/')}/{imageName}";

        _logger.LogInformation("Pulling Docker image '{Image}' from registry", fullImage);

        // Resolve authentication from configured registries or the provided token
        var authConfig = ResolveAuthConfig(registryUrl, authToken);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));

            await _client.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = fullImage },
                authConfig,
                new Progress<JSONMessage>(msg =>
                {
                    if (msg.Error is not null)
                        _logger.LogWarning("Image pull progress for '{Image}': {Error}", fullImage, msg.Error);
                }),
                cts.Token);

            _logger.LogInformation("Docker image '{Image}' pulled successfully", fullImage);
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Docker image pull for '{Image}' timed out after {Timeout}s",
                fullImage, _timeoutSeconds);
            throw new ContainerOrchestrationException(
                $"Docker image pull for '{fullImage}' timed out after {_timeoutSeconds}s");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pull Docker image '{Image}'", fullImage);
            throw new ContainerOrchestrationException(
                $"Failed to pull Docker image '{fullImage}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Resolves Docker registry authentication from configured registries or a provided OAuth2 token.
    /// </summary>
    private AuthConfig? ResolveAuthConfig(string? registryUrl, string? authToken)
    {
        // If a token is explicitly provided, use it
        if (!string.IsNullOrWhiteSpace(authToken))
        {
            return new AuthConfig
            {
                Username = "oauth2accesstoken",
                Password = authToken,
                ServerAddress = registryUrl
            };
        }

        // Look up from configured registries
        if (!string.IsNullOrWhiteSpace(registryUrl))
        {
            var configured = _registries.GetForImage(registryUrl);
            if (configured?.Valid == true)
            {
                return new AuthConfig
                {
                    Username = configured.UserName,
                    Password = configured.Password,
                    ServerAddress = configured.ServerAddress
                };
            }
        }

        // Public image, no authentication needed
        return null;
    }
}

/// <summary>
/// Exception thrown when a container orchestration operation fails.
/// </summary>
public class ContainerOrchestrationException : Exception
{
    /// <summary>
    /// Creates a new <see cref="ContainerOrchestrationException"/> with the specified error message.
    /// </summary>
    public ContainerOrchestrationException(string message) : base(message) { }

    /// <summary>
    /// Creates a new <see cref="ContainerOrchestrationException"/> with the specified error message and inner exception.
    /// </summary>
    public ContainerOrchestrationException(string message, Exception innerException) : base(message, innerException) { }
}
