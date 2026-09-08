using System.Text.Json;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.TeamLab.Contracts.Execution;
using NJsonSchema;

namespace GZCTF.Modules.TeamLab.Application;

internal static class TeamLabDeviceExecutionCompiler
{
    public static void RequireResources(TeamLabDevicePackage package, TeamLabAssetResourceModel resources)
    {
        if ((long)resources.CpuUnits * 1000 < package.CpuMillis || resources.MemoryMiB < package.MemoryMib ||
            (long)resources.StorageMiB < (long)package.StorageGib * 1024)
            throw new TeamLabApiContractException("device_package_resources_insufficient", "资产资源低于设备包的最低要求，请调整 CPU、内存或磁盘容量。", 422);
    }

    public static async Task<TeamLabDeviceExecutionV2> CompileAsync(TeamLabDevicePackage package, TeamLabAssetKind kind,
        string imageDigest, string? parametersJson, CancellationToken token)
    {
        var expectedKind = kind == TeamLabAssetKind.Docker ? TeamLabDevicePackageArtifactKind.OciImage : TeamLabDevicePackageArtifactKind.VmImage;
        if (package.ArtifactKind != expectedKind || string.IsNullOrWhiteSpace(package.Digest) ||
            !NormalizeDigest(package.Digest).Equals(NormalizeDigest(imageDigest), StringComparison.OrdinalIgnoreCase))
            throw new TeamLabApiContractException("device_package_artifact_mismatch", "设备包制品与所选镜像不一致，请选择同一摘要和类型的镜像。", 422);
        var parameters = string.IsNullOrWhiteSpace(parametersJson) ? "{}" : parametersJson;
        using var values = ParseParameters(parameters);
        if (values.RootElement.ValueKind != JsonValueKind.Object || parameters.Length > 2048)
            throw new TeamLabApiContractException("device_package_parameters_invalid", "设备参数必须是 2048 字符以内的 JSON 对象。", 422);
        var schema = await ReadSchemaAsync(package.ParameterSchemaJson, token);
        var errors = schema.Validate(parameters);
        if (errors.Count > 0)
        {
            var fields = string.Join("、", errors.Select(error => error.Path ?? string.Empty).Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct().Take(5).Select(path => path.Length > 64 ? path[..64] : path));
            throw new TeamLabApiContractException("device_package_parameters_invalid", $"设备参数不符合声明{(fields.Length > 0 ? $"（{fields}）" : string.Empty)}，请检查必填项、类型和取值范围。", 422);
        }
        using var health = JsonDocument.Parse(package.HealthDeclarationJson);
        var root = health.RootElement;
        var protocol = root.TryGetProperty("kind", out var healthKind) ? healthKind.GetString() : null;
        var declaredEvents = JsonSerializer.Deserialize<string[]>(package.ProtocolEventTypesJson) ?? [];
        if (declaredEvents.Length > 0 && protocol != "http")
            throw new TeamLabApiContractException("device_package_events_require_http", "自动协议事件采集需要 HTTP 健康端点返回 bootId 和 counters；仅 TCP 端口检查不能提供协议事件。", 422);
        return new(package.PublicId, package.Name, package.Version, $"sha256:{NormalizeDigest(package.Digest).ToLowerInvariant()}",
            JsonSerializer.Serialize(values.RootElement), protocol is "tcp" or "http" ? protocol : null,
            root.TryGetProperty("port", out var port) ? port.GetInt32() : null,
            root.TryGetProperty("path", out var path) ? path.GetString() : null,
            root.TryGetProperty("intervalSeconds", out var interval) ? interval.GetInt32() : 30,
            declaredEvents.Length > 0 ? declaredEvents : null);
    }

    private static string NormalizeDigest(string digest) => digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : digest;

    internal static async Task<JsonSchema> ReadSchemaAsync(string json, CancellationToken token)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            RequireLocalSchema(document.RootElement);
            return await JsonSchema.FromJsonAsync(json, cancellationToken: token);
        }
        catch (Exception exception) when (exception is JsonException or Newtonsoft.Json.JsonException or ArgumentException)
        {
            throw new TeamLabApiContractException("device_package_parameter_schema_invalid", "设备包参数 schema 无法解析。", 422);
        }
    }

    private static JsonDocument ParseParameters(string json)
    {
        try { return JsonDocument.Parse(json); }
        catch (JsonException) { throw new TeamLabApiContractException("device_package_parameters_invalid", "设备参数不是有效 JSON。", 422); }
    }

    private static void RequireLocalSchema(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name == "$ref" && (property.Value.ValueKind != JsonValueKind.String ||
                !property.Value.GetString()!.StartsWith('#')))
                throw new TeamLabApiContractException("device_package_parameter_schema_invalid", "设备参数 schema 仅支持包内引用。", 422);
            RequireLocalSchema(property.Value);
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RequireLocalSchema(child);
    }
}
