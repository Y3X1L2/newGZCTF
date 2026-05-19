using GZCTF.Models.Data;
using GZCTF.Storage;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services;

/// <summary>
/// Orchestrates VM and container creation for multi-stage scenario environments.
/// Determines OS type from image templates and routes to VmManager (Windows) or ContainerOrchestrator (Linux).
/// </summary>
public class EnvironmentService
{
    private readonly VmManager _vmManager;
    private readonly ContainerOrchestrator _containerOrchestrator;
    private readonly GuacamoleProxy _guacamoleProxy;
    private readonly ImageStorage _imageStorage;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<EnvironmentService> _logger;

    public EnvironmentService(
        VmManager vmManager,
        ContainerOrchestrator containerOrchestrator,
        GuacamoleProxy guacamoleProxy,
        ImageStorage imageStorage,
        AppDbContext dbContext,
        ILogger<EnvironmentService> logger)
    {
        _vmManager = vmManager;
        _containerOrchestrator = containerOrchestrator;
        _guacamoleProxy = guacamoleProxy;
        _imageStorage = imageStorage;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Creates the full environment for a stage within a scenario instance.
    /// Determines OS type from image templates, provisions VMs or containers,
    /// creates isolated networks, and generates credentials.
    /// </summary>
    public async Task<StageEnvironmentResult?> CreateStageEnvironmentAsync(
        Stage stage, Guid userId, CancellationToken token = default)
    {
        _logger.LogInformation(
            "Creating stage environment for Stage {StageId} (Scenario {ScenarioId}), User {UserId}",
            stage.Id, stage.ScenarioId, userId);

        try
        {
            var imageTemplateIds = DeserializeImageIds(stage.EnvironmentImageIds);

            if (imageTemplateIds.Length == 0)
            {
                _logger.LogWarning("No image templates configured for Stage {StageId}", stage.Id);
                return new StageEnvironmentResult { StageId = stage.Id };
            }

            var imageTemplates = await _dbContext.ImageTemplates
                .Where(t => imageTemplateIds.Contains(t.Id))
                .ToListAsync(token);

            if (imageTemplates.Count == 0)
            {
                _logger.LogWarning("Image templates not found for Stage {StageId}", stage.Id);
                return new StageEnvironmentResult { StageId = stage.Id };
            }

            var networkName = $"scenario_s{stage.ScenarioId}_st{stage.Id}_u{userId:N}";
            var vmNames = new List<string>();
            var connectionDetails = new List<EnvironmentConnection>();

            // Create isolated Docker network for the stage if network rules are defined
            if (!string.IsNullOrWhiteSpace(stage.NetworkRules))
            {
                await _containerOrchestrator.CreateIsolatedNetwork(networkName);
                _logger.LogInformation("Created isolated network {Network} for Stage {StageId}",
                    networkName, stage.Id);
            }

            foreach (var template in imageTemplates)
            {
                switch (template.OSType)
                {
                    case OSType.Windows:
                    {
                        var vmName = $"scenario-s{stage.ScenarioId}-st{stage.Id}-u{userId:N}"
                            .ToValidRFC1123String("vm");
                        vmNames.Add(vmName);

                        if (template.LocalFilePath is not null)
                        {
                            await _vmManager.CreateFromTemplate(template.LocalFilePath, vmName);
                            await _vmManager.Start(vmName);

                            var vncPort = await _vmManager.GetVncPort(vmName);
                            var ipAddress = await _vmManager.GetIpAddress(vmName);

                            // Create Guacamole RDP connection with dynamic credentials
                            var rdpPort = 3389;
                            var sessionUser = "player";
                            var sessionPass = Codec.RandomPassword(16);
                            var (connectionId, guacToken) = await _guacamoleProxy
                                .CreateConnectionWithCredentialsAsync(vmName, ipAddress ?? "127.0.0.1",
                                    rdpPort, sessionUser, sessionPass);

                            connectionDetails.Add(new EnvironmentConnection
                            {
                                Name = template.Name,
                                Type = "Windows",
                                VmName = vmName,
                                Host = ipAddress,
                                Port = rdpPort,
                                Protocol = "rdp",
                                GuacamoleConnectionId = connectionId,
                                GuacamoleToken = guacToken,
                                GuacamoleUrl = _guacamoleProxy.GetConnectionUrl(connectionId, guacToken)
                            });
                        }

                        break;
                    }
                    case OSType.Linux:
                    {
                        var containerName = $"scenario-s{stage.ScenarioId}-st{stage.Id}-{userId:N}"
                            .ToValidRFC1123String("ctr");

                        if (template.RegistryUrl is not null)
                        {
                            var imageName = template.Name.ToValidRFC1123String("img");
                            await _containerOrchestrator.PullImageFromRegistryAsync(
                                template.RegistryUrl, imageName, template.RegistryAuth);
                        }

                        connectionDetails.Add(new EnvironmentConnection
                        {
                            Name = template.Name,
                            Type = "Linux",
                            ContainerName = containerName,
                            Protocol = "docker"
                        });

                        break;
                    }
                }
            }

            _logger.LogInformation(
                "Stage environment created for Stage {StageId}: {VmCount} VMs, {ConnCount} connections",
                stage.Id, vmNames.Count, connectionDetails.Count);

            return new StageEnvironmentResult
            {
                StageId = stage.Id,
                NetworkName = networkName,
                VmNames = vmNames,
                Connections = connectionDetails,
                Credentials = GenerateCredentials()
            };
        }
        catch (VmOperationException ex)
        {
            _logger.LogError(ex, "VM operation failed creating environment for Stage {StageId}", stage.Id);
            throw;
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Timeout creating environment for Stage {StageId}", stage.Id);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create stage environment for Stage {StageId}", stage.Id);
            throw;
        }
    }

    /// <summary>
    /// Destroys all VMs, containers, and networks associated with a scenario instance stage.
    /// </summary>
    public async Task DestroyStageEnvironmentAsync(Guid instanceId, Stage stage,
        CancellationToken token = default)
    {
        _logger.LogInformation("Destroying environment for Instance {InstanceId}, Stage {StageId}",
            instanceId, stage.Id);

        try
        {
            var instance = await _dbContext.ScenarioInstances
                .FirstOrDefaultAsync(i => i.Id == instanceId, token);

            if (instance is null)
            {
                _logger.LogWarning("Scenario instance {InstanceId} not found for destruction", instanceId);
                return;
            }

            var networkName = $"scenario_s{stage.ScenarioId}_st{stage.Id}_u{instance.UserId:N}";

            // Destroy VMs
            var vmBaseName = $"scenario-s{stage.ScenarioId}-st{stage.Id}-u{instance.UserId:N}"
                .ToValidRFC1123String("vm");

            try
            {
                await _vmManager.Destroy(vmBaseName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to destroy VM {VmName} (may already be destroyed)", vmBaseName);
            }

            // Remove isolated network
            try
            {
                await _containerOrchestrator.RemoveNetwork(networkName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove network {Network} (may already be removed)",
                    networkName);
            }

            _logger.LogInformation("Environment destroyed for Instance {InstanceId}, Stage {StageId}",
                instanceId, stage.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error destroying environment for Instance {InstanceId}, Stage {StageId}",
                instanceId, stage.Id);
        }
    }

    /// <summary>
    /// Resets the environment to initial state by reverting VM snapshots.
    /// </summary>
    public async Task ResetEnvironmentAsync(Guid instanceId, CancellationToken token = default)
    {
        _logger.LogInformation("Resetting environment for Instance {InstanceId}", instanceId);

        try
        {
            var instance = await _dbContext.ScenarioInstances
                .FirstOrDefaultAsync(i => i.Id == instanceId, token);

            if (instance is null)
            {
                _logger.LogWarning("Scenario instance {InstanceId} not found for reset", instanceId);
                return;
            }

            var vmBaseName = $"scenario-s{instance.ScenarioId}-st{instance.CurrentStageId}-u{instance.UserId:N}"
                .ToValidRFC1123String("vm");

            await _vmManager.SnapshotRevert(vmBaseName);

            _logger.LogInformation("Environment reset for Instance {InstanceId}", instanceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset environment for Instance {InstanceId}", instanceId);
            throw;
        }
    }

    private static int[] DeserializeImageIds(string? environmentImageIds)
    {
        if (string.IsNullOrWhiteSpace(environmentImageIds))
            return [];

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<int[]>(environmentImageIds) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static Dictionary<string, string> GenerateCredentials()
    {
        return new Dictionary<string, string>
        {
            ["username"] = "player",
            ["password"] = Codec.RandomPassword(16)
        };
    }
}

/// <summary>
/// Result from creating a stage environment
/// </summary>
public class StageEnvironmentResult
{
    public int StageId { get; set; }
    public string? NetworkName { get; set; }
    public List<string> VmNames { get; set; } = [];
    public List<EnvironmentConnection> Connections { get; set; } = [];
    public Dictionary<string, string> Credentials { get; set; } = [];
}

/// <summary>
/// Connection details for an environment endpoint
/// </summary>
public class EnvironmentConnection
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? VmName { get; set; }
    public string? ContainerName { get; set; }
    public string? Host { get; set; }
    public int? Port { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public string? GuacamoleConnectionId { get; set; }
    public string? GuacamoleToken { get; set; }
    public string? GuacamoleUrl { get; set; }
}
