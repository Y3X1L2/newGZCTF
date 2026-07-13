using System.Security.Claims;
using GZCTF.Middlewares;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GZCTF.Modules.Audit.Infrastructure;

public sealed class AdminMutationAuditFilter(
    OperationalCorrelation correlation,
    IOperationalEventWriter events,
    ILogger<AdminMutationAuditFilter> logger) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        if (!ShouldAudit(context))
        {
            await next();
            return;
        }

        using var correlationScope = correlation.Begin();
        context.HttpContext.Response.Headers["X-GZCTF-Correlation-Id"] = correlation.Ensure().ToString();
        var executed = await next();
        var statusCode = executed.Exception is null
            ? executed.HttpContext.Response.StatusCode
            : StatusCodes.Status500InternalServerError;
        var failed = executed.Exception is not null || statusCode >= 400;
        var descriptor = context.ActionDescriptor as ControllerActionDescriptor;
        var controller = descriptor?.ControllerName ?? "Unknown";
        var action = descriptor?.ActionName ?? context.ActionDescriptor.DisplayName ?? "Unknown";
        var (errorCategory, errorCode, retryable) = Classify(statusCode, executed.Exception);
        var actorUserId = Guid.TryParse(
            context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)
            ? actorId
            : (Guid?)null;
        var dimensions = ResolveDimensions(context);

        try
        {
            await events.AppendAndSaveAsync(new OperationalEventDraft(
                failed
                    ? OperationalEventCodes.Audit.AdminMutationFailed
                    : OperationalEventCodes.Audit.AdminMutationSucceeded,
                failed ? OperationalEventOutcome.Failed : OperationalEventOutcome.Succeeded,
                $"{controller}.{action} {(failed ? "failed" : "succeeded")}.",
                failed ? OperationalEventSeverity.Warning : OperationalEventSeverity.Information,
                correlation.Ensure(),
                errorCategory,
                errorCode,
                retryable,
                new Dictionary<string, object?>
                {
                    ["operation"] = $"{controller}.{action}",
                    ["httpStatus"] = statusCode
                },
                actorUserId,
                GameId: dimensions.GameId,
                CourseId: dimensions.CourseId,
                ChallengeId: dimensions.ChallengeId,
                ImageTemplateId: dimensions.TemplateId,
                WorkerNodeId: dimensions.NodeId,
                SubjectType: controller,
                SubjectId: dimensions.SubjectId,
                ResourceType: dimensions.ResourceType,
                ResourceId: dimensions.SubjectId), context.HttpContext.RequestAborted);
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Failed to persist mutation audit for {Controller}.{Action} correlation {CorrelationId}.",
                controller, action, correlation.Current);
        }
    }

    private static bool ShouldAudit(ActionExecutingContext context)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
            return false;
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
            return false;

        var path = context.HttpContext.Request.Path;
        if (path.StartsWithSegments("/api/open/v1", StringComparison.OrdinalIgnoreCase) ||
            path.Value?.EndsWith("/heartbeat", StringComparison.OrdinalIgnoreCase) == true)
            return false;

        return context.Filters.OfType<RequirePrivilegeAttribute>().Any(filter =>
                   filter is RequireTeacherAttribute or RequireAdminAttribute or RequireSuperAdminAttribute) ||
               context.ActionDescriptor.EndpointMetadata.OfType<RequirePrivilegeAttribute>().Any(filter =>
                   filter is RequireTeacherAttribute or RequireAdminAttribute or RequireSuperAdminAttribute);
    }

    private static (OperationalErrorCategory? Category, string? Code, bool Retryable) Classify(
        int statusCode,
        Exception? exception)
    {
        if (exception is not null || statusCode >= 500)
            return (OperationalErrorCategory.Unknown, "operation.unclassified_failure", false);
        return statusCode switch
        {
            StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden =>
                (OperationalErrorCategory.Authorization, "auth.forbidden", false),
            StatusCodes.Status400BadRequest or StatusCodes.Status422UnprocessableEntity =>
                (OperationalErrorCategory.Validation, "request.invalid", false),
            StatusCodes.Status409Conflict =>
                (OperationalErrorCategory.Conflict, "operation.conflict", false),
            >= 400 => (OperationalErrorCategory.Unknown, $"http.{statusCode}", false),
            _ => (null, null, false)
        };
    }

    private static AuditDimensions ResolveDimensions(ActionExecutingContext context)
    {
        static int? IntRoute(ActionExecutingContext value, string name) =>
            int.TryParse(value.RouteData.Values[name]?.ToString(), out var parsed) ? parsed : null;
        static Guid? GuidRoute(ActionExecutingContext value, string name) =>
            Guid.TryParse(value.RouteData.Values[name]?.ToString(), out var parsed) ? parsed : null;

        var subjectId = context.RouteData.Values["id"]?.ToString()
                        ?? context.RouteData.Values["challengeId"]?.ToString()
                        ?? context.RouteData.Values["templateId"]?.ToString()
                        ?? context.RouteData.Values["nodeId"]?.ToString();
        var resourceType = context.RouteData.Values.ContainsKey("challengeId") ? "challenge"
            : context.RouteData.Values.ContainsKey("templateId") ? "image-template"
            : context.RouteData.Values.ContainsKey("nodeId") ? "worker-node"
            : null;
        return new AuditDimensions(
            IntRoute(context, "gameId"),
            IntRoute(context, "courseId"),
            IntRoute(context, "challengeId"),
            IntRoute(context, "templateId"),
            GuidRoute(context, "nodeId") ?? GuidRoute(context, "id"),
            subjectId,
            resourceType);
    }

    private sealed record AuditDimensions(
        int? GameId,
        int? CourseId,
        int? ChallengeId,
        int? TemplateId,
        Guid? NodeId,
        string? SubjectId,
        string? ResourceType);
}
