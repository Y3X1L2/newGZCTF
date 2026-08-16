using System.Text;
using System.Text.Json;
using GZCTF.Modules.TeamLab.Contracts;

namespace GZCTF.Modules.TeamLab.Application;

/// <summary>
/// Shared validation and canonicalization for the capability resource
/// catalog (device packages, connectors, link policies). JSON is stored in a
/// canonical form so semantic equality is a string comparison.
/// </summary>
internal static class TeamLabCapabilityResourceValidation
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Opaque base64 cursor over the integer primary key, matching the rollout cursor contract.</summary>
    public static int? DecodeIntCursor(string? value, string errorCode, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
            return int.TryParse(decoded, out var id) && id > 0 ? id : throw new FormatException();
        }
        catch (FormatException)
        {
            throw new TeamLabApiContractException(errorCode, message, 400);
        }
    }

    public static string EncodeIntCursor(int id) => Convert.ToBase64String(Encoding.UTF8.GetBytes(id.ToString()));

    public static string Slug(string value, int maxLength, string errorCode, string message)
    {
        var slug = value.Trim().ToLowerInvariant();
        if (slug.Length is < 1 || slug.Length > maxLength ||
            slug.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
            throw new TeamLabApiContractException(errorCode, message, 422);
        return slug;
    }

    public static string Text(string value, int minLength, int maxLength, string errorCode, string message)
    {
        var text = value.Trim();
        if (text.Length < minLength || text.Length > maxLength)
            throw new TeamLabApiContractException(errorCode, message, 422);
        return text;
    }

    /// <summary>Optional bounded text: blank becomes null so storage stays minimal.</summary>
    public static string? OptionalText(string? value, int maxLength, string errorCode, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (text.Length > maxLength)
            throw new TeamLabApiContractException(errorCode, message, 422);
        return text;
    }

    /// <summary>Loose but bounded version tag: digits, letters, dot, plus, minus.</summary>
    public static string Version(string value, string errorCode, string message)
    {
        var version = Text(value, 1, 64, errorCode, message);
        if (version.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '+' and not '-'))
            throw new TeamLabApiContractException(errorCode, message, 422);
        return version;
    }

    public static string Digest(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var digest = value.Trim();
        if (digest.Length != 71 || !digest.StartsWith("sha256:", StringComparison.Ordinal) ||
            digest[7..].Any(character => !Uri.IsHexDigit(character)))
            throw new TeamLabApiContractException(
                "artifact_digest_invalid", "制品摘要必须是 sha256:<64 位十六进制>", 422);
        return digest.ToLowerInvariant();
    }

    public static string CanonicalJson(JsonElement? element, int maxLength, string errorCode, string fallback = "{}")
    {
        if (element is not { } value) return fallback;
        string canonical;
        try
        {
            canonical = JsonSerializer.Serialize(value, Json);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new TeamLabApiContractException(errorCode, "JSON 输入无法序列化", 422);
        }
        if (canonical.Length > maxLength)
            throw new TeamLabApiContractException(errorCode, "JSON 输入超出长度限制", 422);
        return canonical;
    }

    public static string CanonicalJsonObject(Dictionary<string, JsonElement> values, int maxLength, string errorCode)
    {
        var canonical = JsonSerializer.Serialize(values, Json);
        if (canonical.Length > maxLength)
            throw new TeamLabApiContractException(errorCode, "JSON 输入超出长度限制", 422);
        return canonical;
    }

    public static JsonElement ParseJson(string canonical) =>
        JsonDocument.Parse(canonical).RootElement.Clone();

    /// <summary>Canonical JSON array of unique, bounded slug items.</summary>
    public static string StringListJson(IReadOnlyList<string>? values, int maxItems, string errorCode, string message)
    {
        if (values is null || values.Count == 0) return "[]";
        if (values.Count > maxItems)
            throw new TeamLabApiContractException(errorCode, message, 422);
        var items = values
            .Select(value => Slug(value, 64, errorCode, message))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return JsonSerializer.Serialize(items, Json);
    }

    public static IReadOnlyList<string> ParseStringList(string canonical) =>
        JsonSerializer.Deserialize<List<string>>(canonical, Json) ?? [];

    public static Dictionary<string, JsonElement> ToValueDictionary(JsonElement element) =>
        element.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());

    /// <summary>Reads a required bounded number and returns it as double for range checks.</summary>
    public static double RequiredNumber(Dictionary<string, JsonElement> values, string key, string errorCode, string message)
    {
        if (!values.TryGetValue(key, out var element) || element.ValueKind != JsonValueKind.Number ||
            !element.TryGetDouble(out var number))
            throw new TeamLabApiContractException(errorCode, message, 422);
        return number;
    }

    public static double? OptionalNumber(Dictionary<string, JsonElement> values, string key)
    {
        if (!values.TryGetValue(key, out var element) || element.ValueKind != JsonValueKind.Number ||
            !element.TryGetDouble(out var number))
            return null;
        return number;
    }

    public static int RequiredPort(Dictionary<string, JsonElement> values, string key, string errorCode)
    {
        var port = (int)RequiredNumber(values, key, errorCode, "端口必须是 1-65535 的整数");
        if (port is < 1 or > 65535)
            throw new TeamLabApiContractException(errorCode, "端口必须是 1-65535 的整数", 422);
        return port;
    }

    public static string RequiredEnum(Dictionary<string, JsonElement> values, string key, string errorCode, string message, IReadOnlyList<string> allowed)
    {
        if (!values.TryGetValue(key, out var element) || element.ValueKind != JsonValueKind.String)
            throw new TeamLabApiContractException(errorCode, message, 422);
        var text = element.GetString()!.Trim();
        if (allowed.All(candidate => !string.Equals(candidate, text, StringComparison.OrdinalIgnoreCase)))
            throw new TeamLabApiContractException(errorCode, message, 422);
        return allowed.First(candidate => string.Equals(candidate, text, StringComparison.OrdinalIgnoreCase));
    }

    public static string? OptionalCidr(Dictionary<string, JsonElement> values, string key, string errorCode)
    {
        if (!values.TryGetValue(key, out var element) || element.ValueKind is not JsonValueKind.String)
            return null;
        var cidr = element.GetString()!.Trim();
        if (cidr.Length == 0) return null;
        var separator = cidr.IndexOf('/');
        if (separator <= 0 || !System.Net.IPAddress.TryParse(cidr[..separator], out _) ||
            !int.TryParse(cidr[(separator + 1)..], out var prefix) || prefix < 0 || prefix > 128)
            throw new TeamLabApiContractException(errorCode, "CIDR 格式无效", 422);
        return cidr;
    }

    public static string? OptionalAddress(Dictionary<string, JsonElement> values, string key, string errorCode)
    {
        if (!values.TryGetValue(key, out var element) || element.ValueKind != JsonValueKind.String)
            return null;
        var address = element.GetString()!.Trim();
        if (address.Length == 0) return null;
        if (!System.Net.IPAddress.TryParse(address, out _))
            throw new TeamLabApiContractException(errorCode, "地址格式无效", 422);
        return address;
    }
}
