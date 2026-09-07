using System.Net;
using System.Text;
using System.Text.Json;
using GZCTF.Integration.Test.Base;
using Xunit;
using Xunit.Abstractions;

namespace GZCTF.Integration.Test.Tests.Api;

/// <summary>
/// Tests for OpenAPI specification and schema validation
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class OpenApiTests(GZCTFApplicationFactory factory, ITestOutputHelper output)
{
    private const string OpenV1DocumentPath = "/openapi/open-v1.json";
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task OpenApi_Spec_IsValidJson()
    {
        // Act
        var response = await _client.GetAsync("/openapi/v1.json");
        output.WriteLine($"Status: {response.StatusCode}");

        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        if (Environment.GetEnvironmentVariable("OPENAPI_MAIN_CURRENT_PATH") is { Length: > 0 } outputPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            await File.WriteAllTextAsync(outputPath, content);
        }
        output.WriteLine($"Response length: {content.Length} bytes");

        // Assert - should be valid JSON
        Assert.NotEmpty(content);

        // Parse to verify it's valid JSON
        var jsonDoc = JsonDocument.Parse(content);
        Assert.NotNull(jsonDoc);

        // Verify it has OpenAPI structure
        var root = jsonDoc.RootElement;
        Assert.True(root.TryGetProperty("openapi", out var openApiVersion));
        Assert.True(root.TryGetProperty("info", out var info));
        Assert.True(root.TryGetProperty("paths", out var paths));

        output.WriteLine($"OpenAPI version: {openApiVersion.GetString()}");
        output.WriteLine($"Title: {info.GetProperty("title").GetString()}");
        output.WriteLine($"Number of paths: {paths.EnumerateObject().Count()}");
    }

    [Fact]
    public async Task OpenApi_ContainsExpectedEndpoints()
    {
        // Act
        var response = await _client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);
        var paths = jsonDoc.RootElement.GetProperty("paths");

        // Expected endpoints
        string[] expectedEndpoints =
        [
            "/api/Config", "/api/Account/Register", "/api/Account/LogIn", "/api/Account/Profile"
        ];

        // Assert
        foreach (var endpoint in expectedEndpoints)
        {
            var hasEndpoint = paths.EnumerateObject()
                .Any(p => p.Name.Equals(endpoint, StringComparison.OrdinalIgnoreCase));
            output.WriteLine($"Endpoint '{endpoint}': {(hasEndpoint ? "Found" : "Missing")}");
            Assert.True(hasEndpoint, $"Expected endpoint '{endpoint}' not found in OpenAPI spec");
        }
    }

    [Fact]
    public async Task OpenApi_HasSchemaDefinitions()
    {
        // Act
        var response = await _client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        // Assert - should have components/schemas section
        Assert.True(jsonDoc.RootElement.TryGetProperty("components", out var components));
        Assert.True(components.TryGetProperty("schemas", out var schemas));

        var schemaCount = schemas.EnumerateObject().Count();
        output.WriteLine($"Number of schema definitions: {schemaCount}");
        Assert.True(schemaCount > 0, "OpenAPI spec should contain schema definitions");

        // List some schemas for verification
        var schemaNames = schemas.EnumerateObject().Select(s => s.Name).Take(10).ToList();
        output.WriteLine($"Sample schemas: {string.Join(", ", schemaNames)}");
    }

    [Fact]
    public async Task OpenV1_Spec_IsValidJson()
    {
        var response = await _client.GetAsync(OpenV1DocumentPath);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content);

        Assert.StartsWith("3.", document.RootElement.GetProperty("openapi").GetString());
        Assert.True(document.RootElement.TryGetProperty("info", out _));
        Assert.True(document.RootElement.TryGetProperty("paths", out _));
    }

    [Fact]
    public async Task OpenV1_ContainsOnlyExternalRoutes()
    {
        var content = await _client.GetStringAsync(OpenV1DocumentPath);
        using var document = JsonDocument.Parse(content);
        var paths = document.RootElement.GetProperty("paths");

        Assert.NotEmpty(paths.EnumerateObject());
        Assert.All(paths.EnumerateObject(), path =>
            Assert.StartsWith("/api/open/v1/", path.Name, StringComparison.Ordinal));

        Assert.True(paths.GetProperty("/api/open/v1/images/docker-references")
            .TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/open/v1/images/docker-archives")
            .TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/open/v1/operations/{id}")
            .TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/open/v1/training/courses/import")
            .TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/open/v1/theory/questions/import")
            .TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/open/v1/theory/games/{gameId}/paper")
            .TryGetProperty("put", out _));
        Assert.True(paths.GetProperty("/api/open/v1/teams/import")
            .TryGetProperty("post", out _));
    }

    [Fact]
    public async Task OpenV1_UsesGzctfApiTokenBearerAuthentication()
    {
        var content = await _client.GetStringAsync(OpenV1DocumentPath);
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        var scheme = root.GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("GzctfApiToken");

        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());

        foreach (var path in root.GetProperty("paths").EnumerateObject())
        foreach (var operation in path.Value.EnumerateObject()
                     .Where(item => IsHttpMethod(item.Name)))
        {
            var security = operation.Value.GetProperty("security");
            Assert.Contains(security.EnumerateArray(), requirement =>
                requirement.TryGetProperty("GzctfApiToken", out _));
        }
    }

    [Fact]
    public async Task OpenV1_DescribesDockerArchiveAsMultipartBinaryUpload()
    {
        var content = await _client.GetStringAsync(OpenV1DocumentPath);
        using var document = JsonDocument.Parse(content);
        var requestBody = document.RootElement.GetProperty("paths")
            .GetProperty("/api/open/v1/images/docker-archives")
            .GetProperty("post")
            .GetProperty("requestBody");
        Assert.True(requestBody.GetProperty("required").GetBoolean());
        var schema = requestBody
            .GetProperty("content")
            .GetProperty("multipart/form-data")
            .GetProperty("schema");
        var properties = schema.GetProperty("properties");

        Assert.Equal("string", properties.GetProperty("file").GetProperty("type").GetString());
        Assert.Equal("binary", properties.GetProperty("file").GetProperty("format").GetString());
        Assert.True(properties.TryGetProperty("name", out _));
        Assert.False(properties.TryGetProperty("repository", out _));
        Assert.False(properties.TryGetProperty("tag", out _));
        Assert.Contains(schema.GetProperty("required").EnumerateArray(),
            item => item.GetString() == "file");
        Assert.Contains(schema.GetProperty("required").EnumerateArray(),
            item => item.GetString() == "name");
    }

    [Fact]
    public async Task OpenV1_WriteOperationsRequireIdempotencyKeyHeader()
    {
        var content = await _client.GetStringAsync(OpenV1DocumentPath);
        using var document = JsonDocument.Parse(content);
        var paths = document.RootElement.GetProperty("paths");

        var writes = paths.EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(operation => operation.Name is "post" or "put" or "delete")
                .Select(operation => (Route: path.Name, Method: operation.Name, Operation: operation.Value)))
            .Where(item =>
                item.Route is "/api/open/v1/images/docker-references" or "/api/open/v1/images/docker-archives" ||
                item.Route.StartsWith("/api/open/v1/training", StringComparison.Ordinal) ||
                item.Route.StartsWith("/api/open/v1/theory", StringComparison.Ordinal) ||
                item.Route.StartsWith("/api/open/v1/teams", StringComparison.Ordinal) ||
                item.Route.StartsWith("/api/open/v1/teamlab", StringComparison.Ordinal) &&
                item.Operation.GetProperty("responses").TryGetProperty("202", out _))
            .ToArray();

        Assert.NotEmpty(writes);
        foreach (var write in writes)
        {
            var parameters = write.Operation.GetProperty("parameters");
            Assert.True(parameters.EnumerateArray().Any(parameter =>
                parameter.GetProperty("name").GetString() == "Idempotency-Key" &&
                parameter.GetProperty("in").GetString() == "header" &&
                parameter.GetProperty("required").GetBoolean()),
                $"{write.Method.ToUpperInvariant()} {write.Route} must require Idempotency-Key.");
            if (write.Route.StartsWith("/api/open/v1/teamlab", StringComparison.Ordinal) ||
                write.Route.StartsWith("/api/open/v1/training", StringComparison.Ordinal) ||
                write.Route.StartsWith("/api/open/v1/theory", StringComparison.Ordinal) ||
                write.Route.StartsWith("/api/open/v1/teams", StringComparison.Ordinal))
                Assert.True(write.Operation.GetProperty("responses").TryGetProperty("202", out _),
                    $"{write.Method.ToUpperInvariant()} {write.Route} must return 202 Accepted.");
        }
    }

    [Fact]
    public async Task OpenV1_TeamLabSchemasPreserveEditorAndHideSensitiveRuntimeState()
    {
        var content = await _client.GetStringAsync(OpenV1DocumentPath);
        using var document = JsonDocument.Parse(content);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var teamLabSchemas = schemas.EnumerateObject()
            .Where(schema => schema.Name.Contains("TeamLab", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(teamLabSchemas);
        foreach (var schema in teamLabSchemas)
        {
            if (!schema.Value.TryGetProperty("properties", out var properties)) continue;
            Assert.False(properties.TryGetProperty("runtimeResourceId", out _),
                $"{schema.Name} must not expose runtimeResourceId.");
            Assert.False(properties.TryGetProperty("protectedDownloadToken", out _),
                $"{schema.Name} must not expose protectedDownloadToken.");
            Assert.False(properties.TryGetProperty("protectedSecret", out _),
                $"{schema.Name} must not expose protectedSecret.");
        }

        var openRequestSchemas = schemas.EnumerateObject()
            .Where(schema => schema.Name.StartsWith("Open", StringComparison.Ordinal) &&
                              schema.Name.Contains("TeamLabTopologyModel", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(openRequestSchemas);
        Assert.All(openRequestSchemas, schema =>
            Assert.True(schema.Value.GetProperty("properties").TryGetProperty("editor", out _),
                $"{schema.Name} must preserve the published editor layout contract."));

        var problemSchema = schemas.GetProperty("ExternalApiProblemDetailsModel");
        Assert.Contains("\"code\"", problemSchema.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("\"traceId\"", problemSchema.GetRawText(), StringComparison.Ordinal);

        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject()
                     .Where(path => path.Name.StartsWith("/api/open/v1/teamlab", StringComparison.Ordinal)))
        foreach (var operation in path.Value.EnumerateObject().Where(item => IsHttpMethod(item.Name)))
        foreach (var response in operation.Value.GetProperty("responses").EnumerateObject()
                     .Where(item => int.TryParse(item.Name, out var status) && status >= 400))
        {
            var contentTypes = response.Value.GetProperty("content");
            Assert.True(contentTypes.TryGetProperty("application/problem+json", out _),
                $"{operation.Name.ToUpperInvariant()} {path.Name} response {response.Name} must use application/problem+json.");
        }
    }

    [Fact]
    public async Task OpenV1_MatchesCommittedContract()
    {
        var current = await _client.GetStringAsync(OpenV1DocumentPath);
        var contractPath = FindContractPath();
        if (Environment.GetEnvironmentVariable("OPENAPI_CURRENT_PATH") is { Length: > 0 } outputPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            await File.WriteAllTextAsync(outputPath, current);
        }
        var expected = await File.ReadAllTextAsync(contractPath);

        Assert.Equal(NormalizeOpenApi(expected), NormalizeOpenApi(current));
    }

    private static bool IsHttpMethod(string name) => name is
        "get" or "put" or "post" or "delete" or "options" or "head" or "patch" or "trace";

    private static string FindContractPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(
                directory.FullName,
                "docs",
                "commercialization",
                "openapi",
                "open-v1.json");
            if (File.Exists(path))
                return path;

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Unable to locate the committed open-v1 OpenAPI contract.");
    }

    private static string NormalizeOpenApi(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteNormalized(writer, document.RootElement);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteNormalized(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject()
                             .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteNormalized(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteNormalized(writer, item);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
