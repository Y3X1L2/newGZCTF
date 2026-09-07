using System.Net;
using System.Text.Json;
using GZCTF.Extensions.Startup;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

public sealed class OpenApiDocumentationTests
{
    [Fact]
    public async Task Production_ExposesOnlyExternalContractAndHtmlReference()
    {
        var options = new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.FullName,
            EnvironmentName = Environments.Production
        };
        var builder = WebApplication.CreateBuilder(options);
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(Program).Assembly);
        builder.AddOpenApiServices();

        await using var app = builder.Build();
        app.MapOpenApiDocumentation();
        await app.StartAsync();

        using var client = app.GetTestClient();
        var documentResponse = await client.GetAsync("/openapi/open-v1.json");
        documentResponse.EnsureSuccessStatusCode();
        var documentContent = await documentResponse.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(documentContent);
        Assert.All(document.RootElement.GetProperty("paths").EnumerateObject(), path =>
            Assert.StartsWith("/api/open/v1/", path.Name, StringComparison.Ordinal));

        var upload = document.RootElement.GetProperty("paths").GetProperty("/api/open/v1/assets").GetProperty("post");
        var form = upload.GetProperty("requestBody").GetProperty("content")
            .GetProperty("multipart/form-data").GetProperty("schema");
        Assert.Equal("binary", form.GetProperty("properties").GetProperty("file").GetProperty("format").GetString());
        Assert.Contains(form.GetProperty("required").EnumerateArray(), item => item.GetString() == "file");
        Assert.Contains(upload.GetProperty("parameters").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "Content-Digest" && item.GetProperty("required").GetBoolean());

        var internalResponse = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.NotFound, internalResponse.StatusCode);

        var redirectResponse = await client.GetAsync("/api-docs");
        Assert.Equal(HttpStatusCode.Found, redirectResponse.StatusCode);
        Assert.EndsWith("api-docs/", redirectResponse.Headers.Location?.OriginalString);

        var htmlResponse = await client.GetAsync("/api-docs/");
        htmlResponse.EnsureSuccessStatusCode();
        Assert.Equal("text/html", htmlResponse.Content.Headers.ContentType?.MediaType);
        var html = await htmlResponse.Content.ReadAsStringAsync();
        Assert.Contains("YINYU 平台开放 API 文档", html, StringComparison.Ordinal);
        Assert.Contains("开放 API 中文导航", html, StringComparison.Ordinal);
        Assert.Contains("填写 Bearer Token", html, StringComparison.Ordinal);
        Assert.Contains("Idempotency-Key", html, StringComparison.Ordinal);
        Assert.Contains("本页面只读取", html, StringComparison.Ordinal);
        Assert.Contains("TeamLab - Topologies", html, StringComparison.Ordinal);
        Assert.Contains("TeamLab - Runtimes", html, StringComparison.Ordinal);
        Assert.Contains("TeamLab - Traffic and Captures", html, StringComparison.Ordinal);
        Assert.Contains("open-v1", html, StringComparison.Ordinal);
        Assert.Contains("/openapi/open-v1.json", html, StringComparison.Ordinal);
        Assert.DoesNotContain("openapi/v1.json", html, StringComparison.Ordinal);

        if (Environment.GetEnvironmentVariable("OPENAPI_CURRENT_PATH") is { Length: > 0 } outputPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            await File.WriteAllTextAsync(outputPath, documentContent);
        }

        var committedContract = await File.ReadAllTextAsync(FindContractPath());
        Assert.Equal(committedContract, documentContent);
    }

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
}
