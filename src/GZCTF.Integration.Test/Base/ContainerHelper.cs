using System.Net.Sockets;
using Docker.DotNet;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Services.Container.Provider;
using GZCTF.Services.Fleet;
using GZCTF.Utils;
using k8s;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace GZCTF.Integration.Test.Base;

/// <summary>
/// Unified container helper for both Docker and Kubernetes environments.
/// Handles container lifecycle monitoring for integration tests.
/// </summary>
public static class ContainerHelper
{
    private const string Namespace = "gzctf-test";
    private const int MaxAttempts = 30;
    private const int DelayMs = 2000;
    private const int LocalTestNodeCapacity = 10_000;
    private static readonly (string Json, string Hash, NodeCapability Capabilities) LocalTestManifest =
        AgentCapabilityEvaluator.Normalize(new AgentCapabilityManifest(
            "integration-test",
            null,
            AgentCapabilityEvaluator.SupportedManifestSchema,
            [AgentFeatureIds.Docker],
            new AgentExecutionLimits(32, 0, 8, 0),
            new AgentHostFacts(64, 64L * 1024 * 1024 * 1024, 1024L * 1024 * 1024 * 1024),
            DateTimeOffset.UtcNow));

    public static async Task SetLocalNodeSchedulingAsync(
        IServiceProvider serviceProvider,
        bool isSchedulable)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var localNodes = context.WorkerNodes.Where(node => node.IsLocal);
        if (isSchedulable)
        {
            await localNodes.ExecuteUpdateAsync(setters => setters
                .SetProperty(node => node.IsSchedulable, true)
                .SetProperty(node => node.MaxContainers, LocalTestNodeCapacity)
                .SetProperty(node => node.CapabilityManifestSchemaVersion,
                    AgentCapabilityEvaluator.SupportedManifestSchema)
                .SetProperty(node => node.CapabilityManifestJson, LocalTestManifest.Json)
                .SetProperty(node => node.CapabilityHash, LocalTestManifest.Hash));
            return;
        }

        await localNodes.ExecuteUpdateAsync(setters => setters
            .SetProperty(node => node.IsSchedulable, false));
    }

    public static async Task<IAsyncDisposable> EnableLocalNodeSchedulingAsync(
        IServiceProvider serviceProvider)
    {
        await SetLocalNodeSchedulingAsync(serviceProvider, true);
        return new LocalNodeSchedulingScope(serviceProvider);
    }

    private sealed class LocalNodeSchedulingScope(IServiceProvider serviceProvider) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() =>
            await SetLocalNodeSchedulingAsync(serviceProvider, false);
    }

    /// <summary>
    /// Wait for admin test container to be ready
    /// </summary>
    /// <param name="serviceProvider">DI service provider</param>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="output">Test output helper for logging</param>
    /// <exception cref="InvalidOperationException">Thrown when container fails, times out, or not found</exception>
    public static async Task<Container> WaitAdminContainerAsync(IServiceProvider serviceProvider, int challengeId,
        ITestOutputHelper output)
    {
        output.WriteLine($"🔍 Waiting for admin test container for challenge {challengeId}...");
        Container? container = null;
        for (var attempt = 0; attempt < MaxAttempts && container is null; attempt++)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            container = await context.GameChallenges.AsNoTracking()
                .Where(challenge => challenge.Id == challengeId)
                .Select(challenge => challenge.TestContainer)
                .SingleOrDefaultAsync();
            if (container is null)
                await Task.Delay(DelayMs);
        }
        if (container is null)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ticket = await context.DeploymentQueueTickets.AsNoTracking()
                .Where(item => item.Kind == DeploymentQueueKind.ChallengeTestContainer &&
                               item.ChallengeId == challengeId)
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => new { item.Status, item.Stage, item.StageMessage, item.ErrorMessage })
                .FirstOrDefaultAsync();
            throw new InvalidOperationException(
                $"Challenge {challengeId} test container was not created. " +
                $"Ticket={ticket?.Status}/{ticket?.Stage}, stage={ticket?.StageMessage}, error={ticket?.ErrorMessage}");
        }

        output.WriteLine($"📦 Found test container: {container.ContainerId}");

        // Wait for container readiness
        await WaitContainerReadyAsync(serviceProvider, container, output);
        return container;
    }

    /// <summary>
    /// Wait for user container to be ready
    /// </summary>
    /// <param name="serviceProvider">DI service provider</param>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="participationId">Participation ID (team in game)</param>
    /// <param name="output">Test output helper for logging</param>
    /// <exception cref="InvalidOperationException">Thrown when container fails, times out, or not found</exception>
    public static async Task<Container> WaitUserContainerAsync(IServiceProvider serviceProvider, int challengeId,
        int participationId, ITestOutputHelper output)
    {
        output.WriteLine(
            $"🔍 Waiting for user container for challenge {challengeId}, participation {participationId}...");
        Container? container = null;
        for (var attempt = 0; attempt < MaxAttempts && container is null; attempt++)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            container = await context.GameInstances.AsNoTracking()
                .Where(instance => instance.ChallengeId == challengeId && instance.ParticipationId == participationId)
                .Select(instance => instance.Container)
                .SingleOrDefaultAsync();
            if (container is null)
                await Task.Delay(DelayMs);
        }
        if (container is null)
            throw new InvalidOperationException(
                $"No game instance found for challenge {challengeId}, participation {participationId}");

        output.WriteLine($"📦 Found user container: {container.ContainerId}");

        // Wait for container readiness
        await WaitContainerReadyAsync(serviceProvider, container, output);
        return container;
    }

    public static async Task WaitContainerDestroyedAsync(
        IServiceProvider serviceProvider,
        Guid containerId,
        ITestOutputHelper output)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var status = await context.Containers.AsNoTracking()
                .Where(container => container.Id == containerId)
                .Select(container => (ContainerStatus?)container.Status)
                .SingleOrDefaultAsync();
            if (status is null or ContainerStatus.Destroyed)
            {
                output.WriteLine($"✅ Container {containerId} cleanup completed");
                return;
            }
            await Task.Delay(DelayMs);
        }

        throw new InvalidOperationException($"Container {containerId} was not destroyed within the timeout.");
    }

    /// <summary>
    /// Fetch flag from container
    /// NOTE: use `ghcr.io/gzctf/challenge-base/echo:latest`
    /// </summary>
    /// <param name="entry"></param>
    /// <returns></returns>
    public static async Task<string?> FetchFlag(string entry)
    {
        Console.WriteLine($@"🔍 Fetching flag from container entry: {entry}");

        // Parse the Entry field to get IP and port
        // Entry format is either "proxy-id" or "IP:Port"
        // For test environments, use localhost since Docker containers are accessible locally
        var parts = entry.Split(':');

        if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
            return null;

        // Use localhost for test environment instead of the container IP
        var host = parts[0];

        // Try to connect to the container and retrieve the flag
        string? flag = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(host, port);
                await using var stream = client.GetStream();
                // Read the flag from the echo container
                var buffer = new byte[256];
                var bytesRead = await stream.ReadAsync(buffer);
                flag = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                break;
            }
            catch (SocketException) when (attempt < 9)
            {
                // Container might not be ready yet, retry after delay
                await Task.Delay(500);
            }
        }

        // Output the retrieved flag for verification
        Console.WriteLine($@"✅ Successfully retrieved flag from {entry}: {flag}");

        return flag;
    }

    /// <summary>
    /// Internal: Unified container readiness polling for both Docker and Kubernetes
    /// </summary>
    private static async Task WaitContainerReadyAsync(
        IServiceProvider serviceProvider,
        Container container,
        ITestOutputHelper output)
    {
        // Try Kubernetes first (if available)
        var k8sProviderService = serviceProvider.GetService<IContainerProvider<Kubernetes, KubernetesMetadata>>();
        if (k8sProviderService != null)
        {
            await WaitK8sContainerReadyAsync(k8sProviderService, container, output);
            return;
        }

        // Fall back to Docker
        var dockerProviderService = serviceProvider.GetService<IContainerProvider<DockerClient, DockerMetadata>>();
        if (dockerProviderService != null)
        {
            await WaitDockerContainerReadyAsync(dockerProviderService, container, output);
            return;
        }

        throw new InvalidOperationException("Neither Kubernetes nor Docker provider is available");
    }

    /// <summary>
    /// Internal: Poll Kubernetes pod until ready or failed
    /// </summary>
    private static async Task WaitK8sContainerReadyAsync(
        IContainerProvider<Kubernetes, KubernetesMetadata> k8sProvider,
        Container container,
        ITestOutputHelper output)
    {
        var k8sClient = k8sProvider.GetProvider();
        var podName = container.ContainerId;

        output.WriteLine($"🔍 Waiting for Kubernetes pod '{podName}' (Image: {container.Image}) to be ready...");

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                var pod = await k8sClient.CoreV1.ReadNamespacedPodAsync(podName, Namespace);

                var phase = pod.Status?.Phase ?? "Unknown";
                var conditions = pod.Status?.Conditions ?? [];
                var readyCondition = conditions.FirstOrDefault(c => c.Type == "Ready");
                var readyStatus = readyCondition?.Status ?? "False";

                output.WriteLine($"  Attempt {attempt + 1}/{MaxAttempts}: Phase={phase}, Ready={readyStatus}");

                switch (phase)
                {
                    // Check if pod is running and ready
                    case "Running" when readyStatus == "True":
                        output.WriteLine($"✅ Pod '{podName}' is Ready!");
                        return;
                    // Pod has failed
                    case "Failed":
                        {
                            var reason = pod.Status?.Reason ?? "Unknown";
                            var message = pod.Status?.Message ?? "";
                            throw new InvalidOperationException($"Pod '{podName}' Failed: {reason} - {message}");
                        }
                }

                // Avoid delay on the last attempt
                if (attempt < MaxAttempts - 1)
                {
                    await Task.Delay(DelayMs);
                }
            }
            catch (Exception e) when (!(e is InvalidOperationException))
            {
                output.WriteLine($"⚠️ Error checking pod status: {e.Message}");
                if (attempt < MaxAttempts - 1)
                {
                    await Task.Delay(DelayMs);
                }
            }
        }

        throw new InvalidOperationException(
            $"Pod '{podName}' did not reach Ready state within {MaxAttempts * DelayMs / 1000} seconds");
    }

    /// <summary>
    /// Internal: Poll Docker container until ready or failed
    /// </summary>
    private static async Task WaitDockerContainerReadyAsync(
        IContainerProvider<DockerClient, DockerMetadata> dockerProvider,
        Container container,
        ITestOutputHelper output)
    {
        var dockerClient = dockerProvider.GetProvider();
        var containerId = container.ContainerId;

        output.WriteLine($"🔍 Waiting for Docker container '{containerId}' (Image: {container.Image}) to be ready...");

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                var inspection = await dockerClient.Containers.InspectContainerAsync(containerId);
                var state = inspection.State;

                output.WriteLine(
                    $"  Attempt {attempt + 1}/{MaxAttempts}: Running={state.Running}, Status={state.Status}");

                switch (state.Running)
                {
                    // Check if container is running
                    case true:
                        output.WriteLine($"✅ Docker container '{containerId}' is Running!");
                        return;
                    // Container has exited abnormally
                    case false when state.ExitCode != 0:
                        {
                            var error = state.Error ?? "No error message";
                            throw new InvalidOperationException(
                                $"Docker container '{containerId}' exited abnormally (exit code: {state.ExitCode}): {error}");
                        }
                }

                // Avoid delay on the last attempt
                if (attempt < MaxAttempts - 1)
                {
                    await Task.Delay(DelayMs);
                }
            }
            catch (Exception e) when (!(e is InvalidOperationException))
            {
                output.WriteLine($"⚠️ Error checking container status: {e.Message}");
                if (attempt < MaxAttempts - 1)
                {
                    await Task.Delay(DelayMs);
                }
            }
        }

        throw new InvalidOperationException(
            $"Docker container '{containerId}' did not reach Running state within {MaxAttempts * DelayMs / 1000} seconds");
    }
}
