using System.Diagnostics;
using System.Text.RegularExpressions;
using GZCTF.Models.Internal;
using Microsoft.Extensions.Options;

namespace GZCTF.Services;

public sealed record DockerImageUploadResult(string FullImage, string SourceImage, string? ImageId);

public class DockerImageRegistryService
{
    static readonly Regex RepositoryRegex = new("^[a-z0-9]+(?:[._/-][a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static readonly Regex TagRegex = new("^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    readonly DockerRegistrySettings _settings;
    readonly ILogger<DockerImageRegistryService> _logger;

    public DockerImageRegistryService(IOptions<DockerRegistrySettings> options,
        ILogger<DockerImageRegistryService> logger)
    {
        _settings = options.Value;
        _logger = logger;
    }

    public long MaxUploadSizeBytes => _settings.MaxUploadSizeBytes;

    public string RegistryAddress => _settings.NormalizedAddress;

    public string RegistryNamespace => _settings.NormalizedNamespace;

    public int MaxUploadSizeGb => _settings.MaxUploadSizeGb;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(RegistryAddress);

    public string BuildInternalImageReference(string repository, string tag)
    {
        var normalizedRepository = NormalizeRepository(repository);
        var normalizedTag = NormalizeTag(tag);
        var address = _settings.NormalizedAddress;
        var ns = _settings.NormalizedNamespace;

        if (string.IsNullOrWhiteSpace(address))
            throw new InvalidOperationException("Internal Docker registry address is not configured.");

        var path = string.IsNullOrWhiteSpace(ns)
            ? normalizedRepository
            : $"{ns}/{normalizedRepository}";

        return $"{address}/{path}:{normalizedTag}";
    }

    public async Task<DockerImageUploadResult> ImportArchiveAsync(string archivePath, string repository, string tag,
        string? sourceImage, CancellationToken token)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("Docker image archive was not found.", archivePath);

        var targetImage = BuildInternalImageReference(repository, tag);
        var load = await RunDockerAsync(["load", "-i", archivePath], token);
        var loadedImage = string.IsNullOrWhiteSpace(sourceImage)
            ? ParseLoadedImageReference(load.Output)
            : sourceImage.Trim();

        if (string.IsNullOrWhiteSpace(loadedImage))
            throw new InvalidOperationException("Cannot resolve loaded image name. Please provide source image.");

        await RunDockerAsync(["tag", loadedImage, targetImage], token);
        await RunDockerAsync(["push", targetImage], token);

        string? imageId = null;
        try
        {
            var inspect = await RunDockerAsync(["image", "inspect", targetImage, "--format", "{{.Id}}"], token);
            imageId = inspect.Output.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to inspect pushed Docker image {Image}", targetImage);
        }

        return new DockerImageUploadResult(targetImage, loadedImage, imageId);
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
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        _logger.LogInformation("Running docker {Arguments}", string.Join(' ', arguments));
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(token);
        var errorTask = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);

        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            _logger.LogWarning("docker {Arguments} failed with exit code {ExitCode}: {Error}",
                string.Join(' ', arguments), process.ExitCode, error.Trim());
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? $"docker command failed with exit code {process.ExitCode}"
                : error.Trim());
        }

        return new DockerCommandResult(output, error);
    }

    readonly record struct DockerCommandResult(string Output, string Error);
}
