using GZCTF.Agent.Models;

namespace GZCTF.Agent.Middlewares;

public sealed class AgentCorrelationErrorMiddleware(
    RequestDelegate next,
    ILogger<AgentCorrelationErrorMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context.Request);
        context.Response.Headers[AgentProtocolHeaders.CorrelationId] = correlationId;
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["AgentOperation"] = AgentOperation.Resolve(context.Request)
        });

        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            if (context.Response.HasStarted)
                throw;

            var error = AgentErrorClassifier.FromException(context.Request, exception, correlationId);
            logger.LogWarning(exception,
                "Agent operation {Operation} failed with {ErrorCategory}/{ErrorCode}.",
                error.Operation, error.Category, error.Code);
            await WriteAsync(context, error, ResolveStatusCode(exception), context.RequestAborted);
        }
    }

    public static AgentErrorResponse CreateValidationError(HttpRequest request, string message)
    {
        var correlationId = ResolveCorrelationId(request);
        return new AgentErrorResponse(
            "Validation",
            "request.invalid",
            message,
            false,
            AgentOperation.Resolve(request),
            correlationId);
    }

    public static async Task WriteAsync(
        HttpContext context,
        AgentErrorResponse error,
        int statusCode,
        CancellationToken token = default)
    {
        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.Headers[AgentProtocolHeaders.CorrelationId] = error.CorrelationId;
        context.Response.Headers[AgentProtocolHeaders.ErrorCategory] = error.Category;
        context.Response.Headers[AgentProtocolHeaders.ErrorCode] = error.Code;
        context.Response.Headers[AgentProtocolHeaders.Retryable] = error.Retryable ? "true" : "false";
        await context.Response.WriteAsJsonAsync(error, cancellationToken: token);
    }

    private static string ResolveCorrelationId(HttpRequest request)
    {
        var header = request.Headers[AgentProtocolHeaders.CorrelationId].ToString();
        return Guid.TryParse(header, out var parsed) && parsed != Guid.Empty
            ? parsed.ToString()
            : Guid.CreateVersion7().ToString();
    }

    private static int ResolveStatusCode(Exception exception) => exception switch
    {
        AgentOperationException operational => operational.StatusCode,
        ArgumentException or FormatException => StatusCodes.Status400BadRequest,
        FileNotFoundException => StatusCodes.Status404NotFound,
        UnauthorizedAccessException => StatusCodes.Status403Forbidden,
        OperationCanceledException => 499,
        _ => StatusCodes.Status500InternalServerError
    };
}

internal static class AgentErrorClassifier
{
    public static AgentErrorResponse FromException(
        HttpRequest request,
        Exception exception,
        string correlationId)
    {
        var operation = AgentOperation.Resolve(request);
        if (exception is AgentOperationException operational)
            return new AgentErrorResponse(
                operational.Category,
                operational.Code,
                operational.Message,
                operational.Retryable,
                operation,
                correlationId);

        if (exception is ArgumentException or FormatException)
            return new AgentErrorResponse(
                "Validation", "request.invalid", "The Agent request is invalid.", false,
                operation, correlationId);
        if (exception is UnauthorizedAccessException)
            return new AgentErrorResponse(
                "Authorization", "auth.forbidden", "The Agent operation is not authorized.", false,
                operation, correlationId);
        if (exception is OperationCanceledException)
            return new AgentErrorResponse(
                "AgentTransport", "agent.timeout", "The Agent operation timed out or was cancelled.", true,
                operation, correlationId);
        if (exception is FileNotFoundException)
            return new AgentErrorResponse(
                "Storage", "storage.file_not_found", "The requested Agent resource was not found.", false,
                operation, correlationId);

        var (category, code, retryable) = AgentOperation.ClassifyFailure(request.Path);
        return new AgentErrorResponse(
            category,
            code,
            $"Agent operation '{operation}' failed.",
            retryable,
            operation,
            correlationId);
    }
}

internal static class AgentOperation
{
    public static string Resolve(HttpRequest request)
    {
        var segments = request.Path.Value?.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (segments.Length < 2 || !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase))
            return "unknown";

        return segments[1].ToLowerInvariant() switch
        {
            "status" => "status.read",
            "maintenance" => "maintenance.sync",
            "images" when segments.Length >= 3 => segments[2].ToLowerInvariant() switch
            {
                "pull-docker" => "image.docker.pull",
                "docker" => "image.docker.delete",
                "download-vm" => "image.vm.download",
                "vm" => "image.vm.delete",
                "ensure-docker-registry" => "image.registry.ensure",
                "configure-docker-registry" => "image.registry.configure",
                _ => "image.unknown"
            },
            "vms" when segments.Length == 3 &&
                       segments[2].Equals("create", StringComparison.OrdinalIgnoreCase) => "vm.create",
            "vms" when segments.Length >= 4 &&
                       segments[3].Equals("ip", StringComparison.OrdinalIgnoreCase) => "vm.ip.read",
            "vms" when HttpMethods.IsDelete(request.Method) => "vm.destroy",
            "containers" => ResolveContainer(request.Method, segments),
            "teamlab" => ResolveTeamLab(request.Method, segments),
            "runtime" => "runtime.inventory",
            _ => "unknown"
        };
    }

    private static string ResolveContainer(string method, string[] segments)
    {
        if (segments.Length == 3 && segments[2].Equals("create", StringComparison.OrdinalIgnoreCase))
            return "container.create";
        if (segments.Length >= 4 && segments[2].Equals("networks", StringComparison.OrdinalIgnoreCase))
            return "container.network.delete";
        if (segments.Length >= 4 && segments[2].Equals("fabric", StringComparison.OrdinalIgnoreCase))
            return HttpMethods.IsDelete(method) ? "fabric.network.delete" : "fabric.network.apply";
        if (segments.Length >= 5 && segments[3].Equals("fabric", StringComparison.OrdinalIgnoreCase))
            return segments[4].ToLowerInvariant() switch
            {
                "interfaces" => "fabric.interface.attach",
                "forwarding" => "fabric.forwarding.enable",
                "routes" => "fabric.route.apply",
                "probe" => "fabric.probe",
                _ => "fabric.unknown"
            };
        if (segments.Length >= 4 && segments[3].Equals("exec", StringComparison.OrdinalIgnoreCase))
            return "container.exec";
        return HttpMethods.IsDelete(method) ? "container.destroy" : "container.unknown";
    }

    private static string ResolveTeamLab(string method, string[] segments)
    {
        if (segments.Length < 3)
            return "teamlab.unknown";
        var action = string.Join('.', segments.Skip(2).Select(segment =>
            int.TryParse(segment, out _) ? "id" : segment.ToLowerInvariant()));
        return HttpMethods.IsGet(method) && action == "status" ? "teamlab.status" : $"teamlab.{action}";
    }

    public static (string Category, string Code, bool Retryable) ClassifyFailure(PathString path)
    {
        if (path.StartsWithSegments("/api/images/pull-docker", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/api/images/download-vm", StringComparison.OrdinalIgnoreCase))
            return ("ImageTransfer", "image.transfer_failed", true);
        if (path.StartsWithSegments("/api/images", StringComparison.OrdinalIgnoreCase))
            return ("ImageRegistry", "image.registry_failed", true);
        if (path.StartsWithSegments("/api/vms", StringComparison.OrdinalIgnoreCase))
            return ("Kvm", "kvm.operation_failed", true);
        if (path.StartsWithSegments("/api/teamlab", StringComparison.OrdinalIgnoreCase) ||
            path.Value?.Contains("/fabric/", StringComparison.OrdinalIgnoreCase) == true ||
            path.Value?.Contains("/networks/", StringComparison.OrdinalIgnoreCase) == true)
            return ("Network", "network.operation_failed", true);
        if (path.StartsWithSegments("/api/containers", StringComparison.OrdinalIgnoreCase))
            return ("Docker", "docker.operation_failed", true);
        if (path.StartsWithSegments("/api/maintenance", StringComparison.OrdinalIgnoreCase))
            return ("AgentProtocol", "agent.sync_failed", true);
        return ("Unknown", "operation.unclassified_failure", false);
    }
}
