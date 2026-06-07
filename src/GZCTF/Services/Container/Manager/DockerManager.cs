// SPDX-License-Identifier: LicenseRef-GZCTF-Restricted
// Copyright (C) 2022-2025 GZTimeWalker
// Restricted Component - NOT under AGPLv3.
// See licenses/LicenseRef-GZCTF-Restricted.txt

using System.Net;
using System.IO.Compression;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using GZCTF.Services.Container.Provider;
using ContainerStatus = GZCTF.Utils.ContainerStatus;

namespace GZCTF.Services.Container.Manager;

public class DockerManager : IContainerManager, IContainerPatchApplicator
{
    private readonly DockerClient _client;
    private readonly ILogger<DockerManager> _logger;
    private readonly DockerMetadata _meta;
    private static readonly TimeSpan PatchApplyTimeout = TimeSpan.FromSeconds(60);
    private const int MaxExtractedPatchArchiveSize = 64 * 1024 * 1024;

    public DockerManager(IContainerProvider<DockerClient, DockerMetadata> provider, ILogger<DockerManager> logger)
    {
        _logger = logger;
        _meta = provider.GetMetadata();
        _client = provider.GetProvider();

        logger.SystemLog(StaticLocalizer[nameof(Resources.Program.ContainerManager_DockerMode)],
            TaskStatus.Success, LogLevel.Debug);
    }


    public async Task DestroyContainerAsync(Models.Data.Container container, CancellationToken token = default)
    {
        try
        {
            await _client.Containers.RemoveContainerAsync(container.ContainerId,
                new() { Force = true }, token);
        }
        catch (DockerContainerNotFoundException)
        {
            _logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.ContainerManager_ContainerDestroyed),
                    container.LogId],
                TaskStatus.Success, LogLevel.Debug);
        }
        catch (DockerApiException e)
        {
            if (e.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.SystemLog(
                    StaticLocalizer[nameof(Resources.Program.ContainerManager_ContainerDestroyed),
                        container.LogId],
                    TaskStatus.Success, LogLevel.Debug);
            }
            else
            {
                _logger.LogDeletionFailedWithHttpContext(container.LogId, e.StatusCode, e.ResponseBody);
                return;
            }
        }
        catch (Exception e)
        {
            _logger.LogErrorMessage(e,
                StaticLocalizer[nameof(Resources.Program.ContainerManager_ContainerDeletionFailed),
                    container.LogId]);
            return;
        }

        container.Status = ContainerStatus.Destroyed;
    }

    public async Task<Models.Data.Container?> CreateContainerAsync(GZCTF.Models.Internal.ContainerConfig config,
        CancellationToken token = default)
    {
        var imageName = config.Image.Split("/").LastOrDefault()?.Split(":").FirstOrDefault();

        if (string.IsNullOrWhiteSpace(imageName))
        {
            _logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.ContainerManager_UnresolvedImageName), config.Image],
                TaskStatus.Failed, LogLevel.Warning);
            return null;
        }

        var parameters = GetCreateContainerParameters(config);

        if (_meta.ExposePort)
        {
            parameters.ExposedPorts = new Dictionary<string, EmptyStruct> { [config.ExposedPort.ToString()] = new() };
            parameters.HostConfig.PortBindings = new Dictionary<string, IList<PortBinding>>
            {
                // let docker choose a random port, do not use "PublishAllPorts" option
                // reference: https://github.com/moby/moby/blob/master/daemon/libnetwork/portallocator/portallocator.go#L135
                // function: RequestPortsInRange
                // comment:
                //     If portStart and portEnd are 0 it returns
                //     the first free port in the default ephemeral range.
                [config.ExposedPort.ToString()] = [new PortBinding { HostPort = "0" }]
            };
        }

        CreateContainerResponse? containerRes;
        var retry = 0;

    CreateDockerContainer:
        try
        {
            if (retry++ >= 3)
            {
                _logger.SystemLog(
                    StaticLocalizer[nameof(Resources.Program.ContainerManager_ContainerCreationFailed),
                        parameters.Name], TaskStatus.Failed, LogLevel.Information);
                return null;
            }

            containerRes = await _client.Containers.CreateContainerAsync(parameters, token);
        }
        catch (DockerImageNotFoundException)
        {
            _logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.ContainerManager_PullContainerImage), config.Image],
                TaskStatus.Pending, LogLevel.Information);

            var auth = _meta.AuthConfigs.GetForImage(config.Image) ?? new AuthConfig();

            // pull the image and retry
            await _client.Images.CreateImageAsync(new() { FromImage = config.Image }, auth,
                new Progress<JSONMessage>(msg =>
                {
                    Console.WriteLine($@"{msg.Status}|{msg.Progress}|{msg.Error}");
                }), token);

            goto CreateDockerContainer;
        }
        catch (DockerApiException e)
        {
            if (e.StatusCode == HttpStatusCode.Conflict)
            {
                _logger.SystemLog(
                    StaticLocalizer[nameof(Resources.Program.ContainerManager_ContainerExisted),
                        parameters.Name],
                    TaskStatus.Duplicate,
                    LogLevel.Warning);

                // the container already exists, remove it and retry
                try
                {
                    await _client.Containers.RemoveContainerAsync(parameters.Name,
                        new() { Force = true }, token);
                }
                catch (Exception ex)
                {
                    _logger.LogErrorMessage(ex,
                        StaticLocalizer[nameof(Resources.Program.ContainerManager_ContainerDeletionFailed),
                            parameters.Name]);
                    return null;
                }

                goto CreateDockerContainer;
            }

            _logger.LogCreationFailedWithHttpContext(parameters.Name, e.StatusCode, e.ResponseBody);
            return null;
        }
        catch (Exception e)
        {
            _logger.LogErrorMessage(e,
                StaticLocalizer[nameof(Resources.Program.ContainerManager_ContainerCreationFailed),
                    parameters.Name]);
            return null;
        }

        var container = new Models.Data.Container { ContainerId = containerRes.ID, Image = config.Image };

        retry = 0;

        while (true)
        {
            if (retry++ >= 3)
            {
                _logger.SystemLog(
                    StaticLocalizer[
                        nameof(Resources.Program.ContainerManager_ContainerInstanceStartFailed),
                        container.LogId,
                        config.Image.Split("/").LastOrDefault() ?? ""],
                    TaskStatus.Failed, LogLevel.Warning);

                await DestroyContainerAsync(container, token);
                return null;
            }

            var started = await _client.Containers.StartContainerAsync(container.ContainerId,
                new(), token);

            if (started)
                break;

            await Task.Delay(500, token);
        }

        var info = await _client.Containers.InspectContainerAsync(container.ContainerId, token);

        container.Status = info.State.Dead || info.State.OOMKilled || info.State.Restarting
            ? ContainerStatus.Destroyed
            : info.State.Running
                ? ContainerStatus.Running
                : ContainerStatus.Pending;

        if (container.Status != ContainerStatus.Running)
        {
            _logger.SystemLog(
                StaticLocalizer[
                    nameof(Resources.Program.ContainerManager_ContainerInstanceCreationFailedWithError),
                    config.Image.Split("/").LastOrDefault() ?? "", info.State.Error],
                TaskStatus.Failed, LogLevel.Warning);

            await DestroyContainerAsync(container, token);
            return null;
        }

        container.StartedAt = DateTimeOffset.Parse(info.State.StartedAt);
        container.ExpectStopAt = container.StartedAt + TimeSpan.FromHours(2);
        container.IP = info.NetworkSettings.Networks.FirstOrDefault().Value.IPAddress;
        container.Port = config.ExposedPort;
        container.IsProxy = !_meta.ExposePort;

        if (!_meta.ExposePort)
            return container;

        var portString = config.ExposedPort.ToString();
        var bindings = info.NetworkSettings.Ports.Where(kv => kv.Key.StartsWith(portString)).Select(kv => kv.Value)
            .SingleOrDefault();

        if (bindings is not { Count: > 0 })
        {
            _logger.SystemLog(
                StaticLocalizer[
                    nameof(Resources.Program.ContainerManager_ContainerCreationFailed),
                    config.Image.Split("/").LastOrDefault() ?? ""],
                TaskStatus.Failed, LogLevel.Warning);

            await DestroyContainerAsync(container, token);
            return null;
        }

        var port = bindings.First().HostPort;

        if (int.TryParse(port, out var numPort))
            container.PublicPort = numPort;
        else
            _logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.ContainerManager_PortParsingFailed), port],
                TaskStatus.Failed,
                LogLevel.Warning);

        if (!string.IsNullOrEmpty(_meta.PublicEntry))
            container.PublicIP = _meta.PublicEntry;

        return container;
    }

    public async Task<ContainerPatchApplyResult> ApplyPatchAsync(Models.Data.Container container, Stream archive,
        CancellationToken token = default)
    {
        if (container.Status != ContainerStatus.Running)
            return ContainerPatchApplyResult.Failed(null, "Container is not running");

        var patchDir = $"/tmp/gzctf-awdp-{Guid.NewGuid():N}";

        try
        {
            var initResult = await RunExec(container.ContainerId, ["sh", "-c", $"mkdir -p {patchDir}"],
                TimeSpan.FromSeconds(10), token);

            if (!initResult.Succeeded)
                return initResult;

            await using var tarArchive = await DecompressGzipArchive(archive, token);
            await _client.Containers.ExtractArchiveToContainerAsync(container.ContainerId,
                new CopyToContainerParameters { Path = patchDir, AllowOverwriteDirWithFile = false },
                tarArchive, token);

            var command =
                $"cd {patchDir} && chmod +x update.sh && ./update.sh";
            var result = await RunExec(container.ContainerId, ["sh", "-c", command], PatchApplyTimeout, token);

            _ = await RunExec(container.ContainerId, ["sh", "-c", $"rm -rf {patchDir}"],
                TimeSpan.FromSeconds(10), token);

            return result;
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(ex, "Invalid AWDP patch archive for container {ContainerId}", container.ContainerId);
            return ContainerPatchApplyResult.Failed(null, "Invalid patch archive");
        }
        catch (DockerApiException ex)
        {
            _logger.LogWarning(ex, "Docker AWDP patch application failed for container {ContainerId}",
                container.ContainerId);
            return ContainerPatchApplyResult.Failed(null, ex.ResponseBody);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "AWDP patch application failed for container {ContainerId}", container.ContainerId);
            return ContainerPatchApplyResult.Failed(null, ex.Message);
        }
    }

    static async Task<MemoryStream> DecompressGzipArchive(Stream archive, CancellationToken token)
    {
        archive.Position = 0;
        var output = new MemoryStream();
        await using var gzip = new GZipStream(archive, CompressionMode.Decompress, leaveOpen: true);
        var buffer = new byte[8192];

        while (true)
        {
            var read = await gzip.ReadAsync(buffer, token);
            if (read == 0)
                break;

            if (output.Length + read > MaxExtractedPatchArchiveSize)
                throw new InvalidDataException("Patch archive is too large after decompression");

            await output.WriteAsync(buffer.AsMemory(0, read), token);
        }

        output.Position = 0;
        return output;
    }

    async Task<ContainerPatchApplyResult> RunExec(string containerId, IList<string> command, TimeSpan timeout,
        CancellationToken token)
    {
        var exec = await _client.Exec.CreateContainerExecAsync(containerId, new ContainerExecCreateParameters
        {
            AttachStderr = true,
            AttachStdout = true,
            Cmd = command
        }, token);

        using var stream = await _client.Exec.StartContainerExecAsync(exec.ID, new ContainerExecStartParameters
        {
            Detach = false
        }, token);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(timeout);

        var output = new StringBuilder();

        try
        {
            var buffer = new byte[8192];

            while (true)
            {
                var result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, cts.Token);

                if (result.EOF)
                    break;

                if (result.Count > 0 && output.Length < 2048)
                    output.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return ContainerPatchApplyResult.Timeout("Patch execution timed out");
        }

        var inspect = await _client.Exec.InspectContainerExecAsync(exec.ID, token);
        var message = output.ToString().Trim();

        return inspect.ExitCode == 0
            ? ContainerPatchApplyResult.Success(string.IsNullOrWhiteSpace(message) ? "Patch applied" : message)
            : ContainerPatchApplyResult.Failed(inspect.ExitCode,
                string.IsNullOrWhiteSpace(message) ? "Patch command failed" : message);
    }

    private CreateContainerParameters GetCreateContainerParameters(GZCTF.Models.Internal.ContainerConfig config) =>
        new()
        {
            Image = config.Image,
            Labels =
                new Dictionary<string, string>
                {
                    ["TeamId"] = config.TeamId,
                    ["UserId"] = config.UserId.ToString(),
                    ["ChallengeId"] = config.ChallengeId.ToString()
                },
            Name = DockerMetadata.GetName(config),

            // GZCTF_FLAG is Per-team dynamic flag issued & audited by the platform.
            //
            // Compliance & Abuse Notice:
            //
            // These env vars are integral to anti-abuse, audit trails and license compliance under
            // the Restricted License (LicenseRef-GZCTF-Restricted). Unauthorized removal, renaming
            // or semantic alteration can indicate an attempt to bypass license terms or weaken
            // challenge isolation guarantees. Downstream extensions MUST preserve their semantics.
            // Modification without a valid authorization may be treated as misuse.
            //
            // References: NOTICE, LICENSE_ADDENDUM.txt, licenses/LicenseRef-GZCTF-Restricted.txt
            Env = config.Flag is null
                ? [$"GZCTF_TEAM_ID={config.TeamId}"]
                : [$"GZCTF_FLAG={config.Flag}", $"GZCTF_TEAM_ID={config.TeamId}"],
            HostConfig = new()
            {
                Memory = config.MemoryLimit * 1024 * 1024,
                CPUPercent = config.CPUCount * 10,
                NetworkMode = !string.IsNullOrEmpty(config.NetworkName)
                    ? config.NetworkName
                    : _meta.NetworkNames[config.NetworkMode]
            }
        };
}
