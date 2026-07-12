using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Identity.Application;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public sealed class ScopedApiTokenAuthenticationTests(GZCTFApplicationFactory factory)
{
    [Theory]
    [InlineData(ApiTokenScopes.ImagesRead, "GET", "/test/scopes/images-read", 200)]
    [InlineData(ApiTokenScopes.ImagesRead, "POST", "/test/scopes/images-write", 403)]
    [InlineData(ApiTokenScopes.ImagesWrite, "POST", "/test/scopes/images-write", 200)]
    public async Task ExternalApi_EnforcesScope(
        string scope,
        string method,
        string path,
        int expectedStatus)
    {
        var token = await IssueTokenAsync([scope], 60);
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(expectedStatus, (int)response.StatusCode);
    }

    [Fact]
    public async Task ValidApiToken_CannotCallAdministratorController()
    {
        var token = await IssueTokenAsync([ApiTokenScopes.ImagesWrite], 60);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExternalApi_RejectsChangedAndRevokedTokens()
    {
        var issued = await IssueTokenWithIdAsync([ApiTokenScopes.ImagesRead], 60);
        Assert.Equal(HttpStatusCode.Unauthorized,
            await SendReadAsync(issued.PlainTextToken + "x"));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IApiTokenStore>();
            Assert.True(await store.RevokeAsync(
                issued.TokenId,
                issued.CreatorId,
                false,
                CancellationToken.None));
        }

        Assert.Equal(HttpStatusCode.Unauthorized,
            await SendReadAsync(issued.PlainTextToken));
    }

    [Fact]
    public async Task ExternalApi_EnforcesResourceGrant()
    {
        var token = await IssueTokenAsync(
            [ApiTokenScopes.ImagesRead],
            60,
            [new ApiTokenResourceGrantSpec("image", "allowed")]);

        Assert.Equal(HttpStatusCode.OK, await SendResourceAsync(token, "allowed"));
        Assert.Equal(HttpStatusCode.Forbidden, await SendResourceAsync(token, "denied"));
    }

    [Fact]
    public async Task ExternalApi_EnforcesRedisBackedPerTokenQuota()
    {
        var token = await IssueTokenAsync([ApiTokenScopes.ImagesRead], 2);

        Assert.Equal(HttpStatusCode.OK, await SendRateLimitedAsync(token));
        Assert.Equal(HttpStatusCode.OK, await SendRateLimitedAsync(token));
        var third = await SendRateLimitedResponseAsync(token);

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.True(third.Headers.RetryAfter?.Delta.HasValue == true ||
                    third.Headers.TryGetValues("Retry-After", out _));
    }

    [Fact]
    public async Task ExternalApi_FailsClosedWhenQuotaBackendIsUnavailable()
    {
        var token = await IssueTokenAsync([ApiTokenScopes.ImagesRead], 60);
        factory.SetApiTokenRateLimitAvailability(false);
        try
        {
            using var response = await SendRateLimitedResponseAsync(token);
            await AssertExternalProblemAsync(
                response,
                HttpStatusCode.ServiceUnavailable,
                "quota_backend_unavailable");
        }
        finally
        {
            factory.SetApiTokenRateLimitAvailability(true);
        }
    }

    [Fact]
    public async Task ExternalApi_RateLimitsValidTokenBeforeScopeRejection()
    {
        var token = await IssueTokenAsync([ApiTokenScopes.ImagesRead], 2);

        using var first = await SendExternalWriteResponseAsync(token);
        await AssertExternalProblemAsync(
            first,
            HttpStatusCode.Forbidden,
            "insufficient_permission");
        using var second = await SendExternalWriteResponseAsync(token);
        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
        using var third = await SendExternalWriteResponseAsync(token);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    private async Task<HttpStatusCode> SendReadAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/scopes/images-read");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await factory.CreateClient().SendAsync(request)).StatusCode;
    }

    private async Task<HttpStatusCode> SendRateLimitedAsync(string token) =>
        (await SendRateLimitedResponseAsync(token)).StatusCode;

    private async Task<HttpStatusCode> SendResourceAsync(string token, string resourceId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/test/resources/{resourceId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await factory.CreateClient().SendAsync(request)).StatusCode;
    }

    private async Task<HttpResponseMessage> SendExternalWriteResponseAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/open/v1/test/images-write");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await factory.CreateClient().SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendRateLimitedResponseAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/open/v1/test/rate-limit");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await factory.CreateClient().SendAsync(request);
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

    private async Task<string> IssueTokenAsync(
        IReadOnlyCollection<string> scopes,
        int requestsPerMinute,
        IReadOnlyCollection<ApiTokenResourceGrantSpec>? resources = null) =>
        (await IssueTokenWithIdAsync(scopes, requestsPerMinute, resources)).PlainTextToken;

    private async Task<IssuedToken> IssueTokenWithIdAsync(
        IReadOnlyCollection<string> scopes,
        int requestsPerMinute,
        IReadOnlyCollection<ApiTokenResourceGrantSpec>? resources = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var issuer = scope.ServiceProvider.GetRequiredService<ApiTokenIssuer>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new UserInfo
        {
            Id = Guid.CreateVersion7(),
            UserName = $"p1-{suffix}",
            NormalizedUserName = $"P1-{suffix.ToUpperInvariant()}",
            Email = $"phase1-{suffix}@example.test",
            NormalizedEmail = $"PHASE1-{suffix.ToUpperInvariant()}@EXAMPLE.TEST",
            EmailConfirmed = true,
            Role = resources is { Count: > 0 } ? Role.Admin : Role.Teacher,
            RegisterTimeUtc = DateTimeOffset.UtcNow
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var issued = await issuer.IssueAsync(
            new ActorContext(user.Id, user.Role),
            new IssueApiTokenCommand(
                "integration",
                scopes,
                resources ?? [],
                requestsPerMinute,
                DateTimeOffset.UtcNow.AddHours(1)),
            CancellationToken.None);
        return new IssuedToken(issued.PlainTextToken, issued.Token.Id, user.Id);
    }

    private sealed record IssuedToken(string PlainTextToken, Guid TokenId, Guid CreatorId);
}
