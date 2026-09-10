using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models.Internal;
using GZCTF.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Services;

public sealed class GuacamoleRemoteSessionServiceTests
{
    [Fact]
    public async Task Delete_WithoutAuthentication_DoesNotReportSuccess()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.OK));
        var service = Create(handler, authenticated: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync("connection", "user", default));
        Assert.Empty(handler.Paths);
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Delete_SuccessOrAbsent_IsIdempotent(HttpStatusCode status)
    {
        using var handler = new Handler(_ => new(status));
        await Create(handler).DeleteAsync("connection", "user", default);
        Assert.Equal(2, handler.Paths.Count);
    }

    [Fact]
    public async Task Delete_UserFailure_StillAttemptsConnectionAndReportsFailure()
    {
        using var handler = new Handler(request => new(request.RequestUri!.AbsolutePath.Contains("/users/")
            ? HttpStatusCode.Forbidden : HttpStatusCode.NoContent));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(handler).DeleteAsync("connection", "user", default));
        Assert.Contains("/api/session/data/postgresql/connections/connection", handler.Paths);
    }

    [Fact]
    public async Task DeleteSession_InventoryFailure_StillRevokesTemporaryUser()
    {
        var sessionId = Guid.NewGuid();
        using var handler = new Handler(request => new(request.Method == HttpMethod.Get
            ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.NoContent));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(handler).DeleteSessionAsync(sessionId, null, null, default));
        Assert.Contains($"/api/session/data/postgresql/users/tlops_{sessionId:N}", handler.Paths);
    }

    [Fact]
    public async Task DeleteSession_LostCreateResponse_OnlyDeletesMatchingDeterministicNames()
    {
        var sessionId = Guid.NewGuid();
        using var handler = new Handler(request => request.Method == HttpMethod.Get
            ? new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
                { owned = new { name = $"tlops-{sessionId:N}" }, unrelated = new { name = "another-session" } })) }
            : new(HttpStatusCode.NoContent));
        await Create(handler).DeleteSessionAsync(sessionId, null, null, default);
        Assert.Contains("/api/session/data/postgresql/connections/owned", handler.Paths);
        Assert.DoesNotContain("/api/session/data/postgresql/connections/unrelated", handler.Paths);
        Assert.Contains($"/api/session/data/postgresql/users/tlops_{sessionId:N}", handler.Paths);
    }

    [Fact]
    public async Task Delete_Cancellation_IsNotConvertedToSuccess()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        using var handler = new Handler(_ => throw new OperationCanceledException(source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(handler).DeleteAsync("connection", "user", source.Token));
    }

    private static GuacamoleRemoteSessionService Create(Handler handler, bool authenticated = true)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(item => item.CreateClient("GuacamoleClient")).Returns(new HttpClient(handler));
        var settings = Options.Create(new GuacamoleSettings
        {
            GuacamoleApiUrl = "http://guacamole.invalid/api",
            GuacamoleAuthToken = authenticated ? Guid.NewGuid().ToString("N") : null!
        });
        return new(factory.Object, new GuacamoleService(factory.Object, settings, NullLogger<GuacamoleService>.Instance),
            settings, NullLogger<GuacamoleRemoteSessionService>.Instance);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(respond(request));
        }
    }
}
