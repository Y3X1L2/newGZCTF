using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using GZCTF.Infrastructure.Api;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Api;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.Identity.Infrastructure;
using GZCTF.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public sealed class ExternalApiProblemDetailsTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task ExternalApiAuthenticationFailure_ReturnsStableProblemDetails()
    {
        using var response = await factory.CreateClient()
            .GetAsync("/api/open/v1/test/rate-limit");

        await AssertExternalProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "authentication_required");
    }

    [Fact]
    public async Task RateLimitMiddlewareMissingTokenClaims_ReturnsStableProblemDetails()
    {
        var context = CreateClaimMissingContext();
        var middleware = new ApiTokenRateLimitMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, null!, null!);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("authentication_required", document.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            document.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task OperationsControllerMissingTokenClaim_ReturnsStableProblemDetails()
    {
        var context = CreateClaimMissingContext();
        var controller = new OperationsController(null!)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };

        var result = await controller.Get(Guid.CreateVersion7(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);
        Assert.Contains("application/problem+json", objectResult.ContentTypes);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal("authentication_required", problem.Extensions["code"]?.ToString());
        Assert.False(string.IsNullOrWhiteSpace(problem.Extensions["traceId"]?.ToString()));
    }

    [Fact]
    public async Task KnownExternalApiException_ReturnsStableProblemDetails()
    {
        using var response = await SendAsync("/api/open/v1/test/problems/conflict");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Equal("idempotency_conflict", problem?["code"].ToString());
        Assert.False(string.IsNullOrWhiteSpace(problem?["traceId"].ToString()));
    }

    [Fact]
    public async Task UnknownExternalApiException_DoesNotLeakInternalDetail()
    {
        using var response = await SendAsync("/api/open/v1/test/problems/unknown");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("internal_error", body);
        Assert.DoesNotContain("phase-one-sensitive-detail", body);
    }

    [Fact]
    public async Task UnknownExternalApiRoute_ReturnsProblemDetailsInsteadOfSpaIndex()
    {
        using var response = await SendAsync("/api/open/v1/not-a-real-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("endpoint_not_found", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task InvalidExternalApiModel_ReturnsValidationProblemDetails()
    {
        var token = await IssueTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/open/v1/test/model-validation")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("validation_failed", await response.Content.ReadAsStringAsync());
    }

    private async Task<HttpResponseMessage> SendAsync(string path)
    {
        var token = await IssueTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await factory.CreateClient().SendAsync(request);
    }

    private static DefaultHttpContext CreateClaimMissingContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/open/v1/test/claim-missing";
        context.Response.Body = new MemoryStream();
        context.TraceIdentifier = Guid.NewGuid().ToString("N");
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ApiTokenClaimTypes.ActorType, "api_token")],
            "test"));
        return context;
    }

    private static async Task AssertExternalProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        Assert.NotNull(problem);
        Assert.Equal(expectedCode, problem["code"].GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem["traceId"].GetString()));
    }

    private async Task<string> IssueTokenAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var issuer = scope.ServiceProvider.GetRequiredService<ApiTokenIssuer>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new UserInfo
        {
            Id = Guid.CreateVersion7(),
            UserName = $"problem-{suffix}",
            NormalizedUserName = $"PROBLEM-{suffix.ToUpperInvariant()}",
            Email = $"problem-{suffix}@example.test",
            NormalizedEmail = $"PROBLEM-{suffix.ToUpperInvariant()}@EXAMPLE.TEST",
            EmailConfirmed = true,
            Role = Role.Teacher,
            RegisterTimeUtc = DateTimeOffset.UtcNow
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var issued = await issuer.IssueAsync(
            new ActorContext(user.Id, user.Role),
            new IssueApiTokenCommand(
                "problem details",
                [ApiTokenScopes.ImagesRead],
                [],
                60,
                DateTimeOffset.UtcNow.AddHours(1)),
            CancellationToken.None);
        return issued.PlainTextToken;
    }
}
