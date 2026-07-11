using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using GZCTF.Models;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Identity.Application;
using Microsoft.AspNetCore.Routing;

namespace GZCTF.Modules.Audit.Infrastructure;

public sealed class ExternalApiRequestAuditMiddleware(
    RequestDelegate next,
    IServiceScopeFactory scopeFactory,
    ILogger<ExternalApiRequestAuditMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ExternalApiAuditContext auditContext)
    {
        if (!context.Request.Path.StartsWithSegments("/api/open/v1", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        var originalBody = context.Response.Body;
        var countingBody = new CountingWriteStream(originalBody);
        context.Response.Body = countingBody;
        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
            await PersistAsync(
                context,
                auditContext,
                countingBody.BytesWritten,
                Stopwatch.GetElapsedTime(started),
                logger,
                scopeFactory);
        }
    }

    private static async Task PersistAsync(
        HttpContext context,
        ExternalApiAuditContext auditContext,
        long responseBytes,
        TimeSpan elapsed,
        ILogger logger,
        IServiceScopeFactory scopeFactory)
    {
        try
        {
            var routePattern = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
            var routeKey = $"{context.Request.Method}:{NormalizeRoute(routePattern, context.Request.Path)}";
            var tokenId = ParseGuidClaim(context.User, ApiTokenClaimTypes.TokenId);
            var actorId = ParseGuidClaim(context.User, ClaimTypes.NameIdentifier);
            var scopes = string.Join(' ', context.User.FindAll(ApiTokenClaimTypes.Scope)
                .Select(claim => claim.Value)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
            var (resourceType, resourceId) = ResolveResource(context);
            var remoteIp = NormalizeIp(context.Connection.RemoteIpAddress);

            await using var scope = scopeFactory.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.ExternalApiRequestAudits.Add(new ExternalApiRequestAudit
            {
                TraceId = Activity.Current?.Id ?? context.TraceIdentifier,
                OperationId = auditContext.OperationId,
                ApiTokenId = tokenId,
                ActorUserId = actorId,
                Scopes = scopes,
                Method = context.Request.Method,
                RouteKey = routeKey,
                ResourceType = resourceType,
                ResourceId = resourceId,
                StatusCode = context.Response.StatusCode,
                ErrorCode = auditContext.ErrorCode ?? DefaultErrorCode(context.Response.StatusCode),
                RequestBytes = Math.Max(0, context.Request.ContentLength ?? 0),
                ResponseBytes = responseBytes,
                RemoteIp = remoteIp,
                IdempotencyReused = auditContext.IdempotencyReused,
                DurationMilliseconds = Math.Max(0, (long)elapsed.TotalMilliseconds),
                CreatedAt = DateTimeOffset.UtcNow
            });
            await database.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Failed to persist external API request audit for trace {TraceId}",
                context.TraceIdentifier);
        }
    }

    private static string NormalizeRoute(string? routePattern, PathString requestPath)
    {
        var route = string.IsNullOrWhiteSpace(routePattern) ? requestPath.Value ?? "/api/open/v1" : routePattern;
        return route.StartsWith('/') ? route : $"/{route}";
    }

    private static (string? Type, string? Id) ResolveResource(HttpContext context)
    {
        var id = context.Request.RouteValues["imageTemplateId"]?.ToString() ??
                 context.Request.RouteValues["id"]?.ToString();
        if (id is null)
            return (null, null);
        return context.Request.Path.StartsWithSegments("/api/open/v1/images", StringComparison.OrdinalIgnoreCase)
            ? ("image", id)
            : context.Request.Path.StartsWithSegments("/api/open/v1/operations", StringComparison.OrdinalIgnoreCase)
                ? ("operation", id)
                : (null, id);
    }

    private static Guid? ParseGuidClaim(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value) ? value : null;

    private static string? NormalizeIp(IPAddress? address) =>
        address?.IsIPv4MappedToIPv6 == true ? address.MapToIPv4().ToString() : address?.ToString();

    private static string? DefaultErrorCode(int statusCode) =>
        statusCode >= 400 ? $"http_{statusCode}" : null;

    private sealed class CountingWriteStream(Stream inner) : Stream
    {
        public long BytesWritten { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => BytesWritten;
        public override long Position
        {
            get => BytesWritten;
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);
            BytesWritten += count;
        }

        public override async Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            await inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
            BytesWritten += count;
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(buffer, cancellationToken);
            BytesWritten += buffer.Length;
        }
    }
}
