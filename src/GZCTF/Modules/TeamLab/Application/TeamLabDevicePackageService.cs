using System.Text.Json;
using GZCTF.Models;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;
using static GZCTF.Modules.TeamLab.Application.TeamLabCapabilityResourceValidation;

namespace GZCTF.Modules.TeamLab.Application;

/// <summary>
/// Device package catalog: registers immutable versions produced by the
/// external artifact pipeline and serves the author/external query surface.
/// Adding a package never requires changing a TeamLab controller.
/// </summary>
public sealed class TeamLabDevicePackageService(AppDbContext context)
{
    private static readonly string[] SupportedAssetKinds = ["docker", "vm"];
    private static readonly string[] HealthKinds = ["none", "tcp", "http"];
    private static readonly string[] PortProtocols = ["tcp", "udp"];

    public async Task<TeamLabDevicePackageModel> RegisterAsync(
        RegisterTeamLabDevicePackageModel command,
        CancellationToken cancellationToken)
    {
        var name = Slug(command.Name, 96, "device_package_name_invalid", "设备包名称无效");
        var displayName = Text(command.DisplayName, 1, 128, "device_package_display_name_invalid", "设备包显示名称无效");
        var version = Version(command.Version, "device_package_version_invalid", "设备包版本号无效");
        if (!TeamLabCapabilityResourceContractMapper.TryParseArtifactKind(command.ArtifactKind, out var artifactKind))
            throw new TeamLabApiContractException("device_package_artifact_kind_invalid", "设备包制品类型无效", 422);
        var reference = Text(command.ArtifactReference, 1, 512, "device_package_artifact_reference_invalid", "设备包制品引用无效");
        if (reference.Any(char.IsWhiteSpace))
            throw new TeamLabApiContractException("device_package_artifact_reference_invalid", "设备包制品引用不能包含空白字符", 422);
        var digest = Digest(command.Digest);
        var assetKinds = ParseAssetKinds(command.SupportedAssetKinds);
        var ports = ParsePorts(command.Ports);
        var parameters = ParameterSchema(command.ParameterSchema);
        var health = HealthDeclaration(command.HealthDeclaration);
        var eventTypes = StringListJson(
            command.ProtocolEventTypes, 32, "device_package_protocol_event_types_invalid", "设备包协议事件类型无效");
        if (command.CpuMillis < 0 || command.MemoryMib < 0 || command.StorageGib < 0)
            throw new TeamLabApiContractException("device_package_resources_invalid", "设备包资源需求不能为负数", 422);

        if (await context.TeamLabDevicePackages.AnyAsync(
                item => item.Name == name && item.Version == version, cancellationToken))
            throw new TeamLabApiContractException("device_package_version_conflict", "该设备包版本已存在", 409);

        var package = new TeamLabDevicePackage
        {
            Name = name,
            DisplayName = displayName,
            Version = version,
            ArtifactKind = artifactKind,
            ArtifactReference = reference,
            Digest = digest.Length == 0 ? null : digest,
            Description = OptionalText(
                command.Description, 2048, "device_package_description_invalid", "设备包描述超出长度限制"),
            SupportedAssetKindsJson = JsonSerializer.Serialize(assetKinds),
            CpuMillis = command.CpuMillis,
            MemoryMib = command.MemoryMib,
            StorageGib = command.StorageGib,
            PortsJson = JsonSerializer.Serialize(ports),
            ParameterSchemaJson = parameters,
            HealthDeclarationJson = health,
            ProtocolEventTypesJson = eventTypes
        };
        context.TeamLabDevicePackages.Add(package);
        await context.SaveChangesAsync(cancellationToken);
        return ToModel(package);
    }

    public async Task<TeamLabDevicePackagePageModel> ListAsync(
        string? name,
        string? after,
        int limit,
        CancellationToken cancellationToken)
    {
        var cursor = DecodeIntCursor(after, "device_package_cursor_invalid", "设备包 cursor 无效");
        var take = Math.Clamp(limit, 1, 100);
        var query = context.TeamLabDevicePackages.AsNoTracking()
            .Where(item => !item.IsArchived);
        if (!string.IsNullOrWhiteSpace(name))
            query = query.Where(item => item.Name == name.Trim().ToLowerInvariant());
        if (cursor is not null)
            query = query.Where(item => item.Id > cursor);
        var rows = await query.OrderBy(item => item.Id).Take(take + 1).ToArrayAsync(cancellationToken);
        return new TeamLabDevicePackagePageModel(
            rows.Take(take).Select(ToModel).ToArray(),
            rows.Length > take ? EncodeIntCursor(rows[take - 1].Id) : null);
    }

    public async Task<TeamLabDevicePackageModel> GetAsync(Guid publicId, CancellationToken cancellationToken)
    {
        var package = await context.TeamLabDevicePackages.AsNoTracking()
            .SingleOrDefaultAsync(item => item.PublicId == publicId, cancellationToken)
            ?? throw new TeamLabApiContractException("device_package_not_found", "未找到设备包", 404);
        if (package.IsArchived)
            throw new TeamLabApiContractException("device_package_not_found", "未找到设备包", 404);
        return ToModel(package);
    }

    public async Task<TeamLabDevicePackageModel> SetEnabledAsync(
        Guid publicId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var package = await RequireWritableAsync(publicId, cancellationToken);
        package.IsEnabled = enabled;
        package.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return ToModel(package);
    }

    public async Task ArchiveAsync(Guid publicId, CancellationToken cancellationToken)
    {
        var package = await RequireWritableAsync(publicId, cancellationToken);
        package.IsArchived = true;
        package.IsEnabled = false;
        package.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<TeamLabDevicePackage> RequireWritableAsync(Guid publicId, CancellationToken cancellationToken) =>
        await context.TeamLabDevicePackages
            .SingleOrDefaultAsync(item => item.PublicId == publicId, cancellationToken)
            ?? throw new TeamLabApiContractException("device_package_not_found", "未找到设备包", 404);

    internal static TeamLabDevicePackageModel ToModel(TeamLabDevicePackage package) => new(
        package.PublicId,
        package.Name,
        package.DisplayName,
        package.Version,
        TeamLabCapabilityResourceContractMapper.ArtifactKindName(package.ArtifactKind),
        package.ArtifactReference,
        package.Digest,
        package.Description,
        ParseStringList(package.SupportedAssetKindsJson),
        package.CpuMillis,
        package.MemoryMib,
        package.StorageGib,
        JsonSerializer.Deserialize<List<TeamLabDevicePackagePortModel>>(package.PortsJson) ?? [],
        ParseJson(package.ParameterSchemaJson),
        ParseJson(package.HealthDeclarationJson),
        ParseStringList(package.ProtocolEventTypesJson),
        package.IsEnabled,
        package.IsArchived,
        package.CreatedAt,
        package.UpdatedAt);

    private static IReadOnlyList<string> ParseAssetKinds(IReadOnlyList<string>? kinds)
    {
        if (kinds is null || kinds.Count == 0)
            throw new TeamLabApiContractException("device_package_asset_kinds_invalid", "设备包必须声明至少一种支持的资产类型", 422);
        var normalized = kinds
            .Select(kind => kind.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalized.Any(kind => !SupportedAssetKinds.Contains(kind)))
            throw new TeamLabApiContractException("device_package_asset_kinds_invalid", "设备包资产类型必须是 docker 或 vm", 422);
        return normalized;
    }

    private static IReadOnlyList<TeamLabDevicePackagePortModel> ParsePorts(IReadOnlyList<TeamLabDevicePackagePortModel>? ports)
    {
        if (ports is null || ports.Count == 0) return [];
        if (ports.Count > 32)
            throw new TeamLabApiContractException("device_package_ports_invalid", "设备包端口声明数量超出限制", 422);
        var result = ports.Select(ParsePort).ToArray();
        if (result.GroupBy(port => (port.Port, port.Protocol)).Any(group => group.Count() > 1))
            throw new TeamLabApiContractException("device_package_ports_invalid", "设备包端口声明重复", 422);
        return result;
    }

    private static TeamLabDevicePackagePortModel ParsePort(TeamLabDevicePackagePortModel port)
    {
        var name = Text(port.Name, 1, 64, "device_package_ports_invalid", "设备包端口名称无效");
        if (port.Port is < 1 or > 65535)
            throw new TeamLabApiContractException("device_package_ports_invalid", "设备包端口必须是 1-65535", 422);
        var protocol = port.Protocol.Trim().ToLowerInvariant();
        if (!PortProtocols.Contains(protocol))
            throw new TeamLabApiContractException("device_package_ports_invalid", "设备包端口协议必须是 tcp 或 udp", 422);
        return new TeamLabDevicePackagePortModel(name, port.Port, protocol);
    }

    /// <summary>Validates the public author parameter schema; content is opaque to the platform.</summary>
    private static string ParameterSchema(JsonElement? schema)
    {
        if (schema is not { } element) return "{}";
        if (element.ValueKind is not JsonValueKind.Object)
            throw new TeamLabApiContractException("device_package_parameter_schema_invalid", "设备包参数 schema 必须是 JSON 对象", 422);
        return CanonicalJson(element, 8192, "device_package_parameter_schema_invalid");
    }

    private static string HealthDeclaration(JsonElement? declaration)
    {
        if (declaration is not { } element || element.ValueKind is not JsonValueKind.Object)
            return "{}";
        var values = ToValueDictionary(element);
        var kind = RequiredEnum(values, "kind", "device_package_health_invalid", "设备包健康声明类型必须是 none、tcp 或 http", HealthKinds);
        var canonical = new Dictionary<string, JsonElement>();
        canonical["kind"] = JsonSerializer.SerializeToElement(kind);
        if (kind != "none")
        {
            if (!values.TryGetValue("port", out var port) || port.ValueKind != JsonValueKind.Number || !port.TryGetInt32(out var portValue) || portValue is < 1 or > 65535)
                throw new TeamLabApiContractException("device_package_health_invalid", "tcp/http 健康检查必须声明 1-65535 的端口", 422);
            canonical["port"] = port;
        }
        if (kind == "http")
        {
            if (!values.TryGetValue("path", out var path) || path.ValueKind != JsonValueKind.String)
                throw new TeamLabApiContractException("device_package_health_invalid", "http 健康检查必须声明 path", 422);
            var pathValue = path.GetString()!.Trim();
            if (pathValue.Length == 0 || pathValue.Length > 256 || !pathValue.StartsWith('/'))
                throw new TeamLabApiContractException("device_package_health_invalid", "http 健康检查 path 必须以 / 开头且不超过 256 字符", 422);
            canonical["path"] = JsonSerializer.SerializeToElement(pathValue);
        }
        if (values.TryGetValue("intervalSeconds", out var interval) && interval.ValueKind == JsonValueKind.Number && interval.TryGetInt32(out var intervalValue))
        {
            if (intervalValue is < 1 or > 3600)
                throw new TeamLabApiContractException("device_package_health_invalid", "健康检查间隔必须是 1-3600 秒", 422);
            canonical["intervalSeconds"] = interval;
        }
        return CanonicalJsonObject(canonical, 1024, "device_package_health_invalid");
    }

}
