namespace GZCTF.Utils;

public readonly record struct DockerImagePullTarget(string RegistryUrl, string ImageName)
{
    public string FullImage => string.IsNullOrWhiteSpace(RegistryUrl)
        ? ImageName
        : $"{RegistryUrl.TrimEnd('/')}/{ImageName.TrimStart('/')}";
}

public static class DockerImageReference
{
    public static DockerImagePullTarget ResolvePullTarget(string imageName, string? registryOrImage)
    {
        var name = imageName.Trim();
        var registry = NormalizeRegistryInput(registryOrImage);

        if (string.IsNullOrWhiteSpace(registry))
            return new DockerImagePullTarget(string.Empty, name);

        return IsRegistryPrefix(registry)
            ? new DockerImagePullTarget(registry, name)
            : new DockerImagePullTarget(string.Empty, registry);
    }

    static string NormalizeRegistryInput(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var input = value.Trim().TrimEnd('/');
        if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return input["http://".Length..];
        if (input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return input["https://".Length..];

        return input;
    }

    static bool IsRegistryPrefix(string value)
    {
        if (!value.Contains('/'))
            return LooksLikeRegistryHost(value);

        // Backwards compatibility for the old two-field form:
        // Registry URL = docker.io/library, Image name = busybox:latest.
        return value.Equals("docker.io/library", StringComparison.OrdinalIgnoreCase)
               || value.Equals("registry-1.docker.io/library", StringComparison.OrdinalIgnoreCase);
    }

    static bool LooksLikeRegistryHost(string value)
    {
        if (value.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        if (value.Contains('.'))
            return true;

        var colon = value.LastIndexOf(':');
        return colon > 0
               && colon < value.Length - 1
               && int.TryParse(value[(colon + 1)..], out _);
    }
}
