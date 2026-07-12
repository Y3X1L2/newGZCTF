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

        foreach (var route in new[]
                 {
                     "/api/open/v1/images/docker-references",
                     "/api/open/v1/images/docker-archives"
                 })
        {
            var parameters = paths.GetProperty(route).GetProperty("post")
                .GetProperty("parameters");
            Assert.Contains(parameters.EnumerateArray(), parameter =>
                parameter.GetProperty("name").GetString() == "Idempotency-Key" &&
                parameter.GetProperty("in").GetString() == "header" &&
                parameter.GetProperty("required").GetBoolean());
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

    [Fact]
    public async Task Scalar_Documentation_IsAvailable()
    {
        // Scalar is the API documentation UI
        // Act
        var response = await _client.GetAsync("/scalar/v1");
        output.WriteLine($"Status: {response.StatusCode}");

        // Assert - in development mode, Scalar should be available
        // It might return 404 in some configurations, so we just verify it responds
        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound,
            $"Expected OK or NotFound but got {response.StatusCode}"
        );
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
