using System.Diagnostics;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;

namespace GZCTF.Infrastructure.Telemetry;

public sealed class AgentTelemetryHandler(OperationalCorrelation correlation) : DelegatingHandler
{
    public const string WorkerNodeHeaderName = "X-GZCTF-Worker-Node-Id";
    public const string ErrorCategoryHeaderName = "X-GZCTF-Error-Category";
    public const string ErrorCodeHeaderName = "X-GZCTF-Error-Code";
    public const string RetryableHeaderName = "X-GZCTF-Retryable";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var operation = AgentOperationName.Resolve(request.Method, request.RequestUri?.AbsolutePath);
        var correlationId = correlation.Current ?? Guid.CreateVersion7();
        using var correlationScope = correlation.Current.HasValue ? null : correlation.Begin(correlationId);
        request.Headers.Remove(OperationalCorrelation.HeaderName);
        request.Headers.TryAddWithoutValidation(OperationalCorrelation.HeaderName, correlationId.ToString());

        using var activity = PlatformTelemetry.AgentClientActivitySource.StartActivity(
            $"agent.{operation}", ActivityKind.Client);
        activity?.SetTag("agent.operation", operation);
        activity?.SetTag("server.address", request.RequestUri?.Host);
        activity?.SetTag("server.port", request.RequestUri?.Port);
        activity?.SetTag("http.request.method", request.Method.Method);
        activity?.SetTag("gzctf.correlation_id", correlationId.ToString());
        if (request.Headers.TryGetValues(WorkerNodeHeaderName, out var nodeIds))
            activity?.SetTag("gzctf.worker_node_id", nodeIds.FirstOrDefault());

        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            var success = response.IsSuccessStatusCode;
            var errorCategory = success ? null : ResolveErrorCategory(response, operation);
            activity?.SetTag("http.response.status_code", (int)response.StatusCode);
            activity?.SetTag("agent.result", success ? "success" : "failure");
            if (!success)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                activity?.SetTag("error.type", ReadHeader(response, ErrorCodeHeaderName) ??
                                                OperationalErrorCodes.UnclassifiedFailure);
                activity?.SetTag("error.category", errorCategory?.ToString());
            }

            PlatformTelemetry.RecordAgentCall(operation, success,
                Stopwatch.GetElapsedTime(startedAt), errorCategory);
            return response;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            var error = OperationalErrorClassifier.FromException(exception, operation);
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("agent.result", "failure");
            activity?.SetTag("error.type", error.Code);
            activity?.SetTag("error.category", error.Category.ToString());
            PlatformTelemetry.RecordAgentCall(operation, false,
                Stopwatch.GetElapsedTime(startedAt), error.Category);
            throw;
        }
    }

    private static OperationalErrorCategory? ResolveErrorCategory(
        HttpResponseMessage response,
        string operation)
    {
        var value = ReadHeader(response, ErrorCategoryHeaderName);
        if (Enum.TryParse<OperationalErrorCategory>(value, true, out var category))
            return category;
        return OperationalErrorClassifier.FromHttpStatus(
            (int)response.StatusCode,
            operation,
            "Agent request failed.").Category;
    }

    private static string? ReadHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}

public static class AgentOperationName
{
    public static string Resolve(HttpMethod method, string? absolutePath)
    {
        var segments = (absolutePath ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2 || !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase))
            return "unknown";

        return segments[1].ToLowerInvariant() switch
        {
            "status" => "status.read",
            "maintenance" => "maintenance.sync",
            "images" => ResolveImage(segments, method),
            "vms" => ResolveVm(segments, method),
            "containers" => ResolveContainer(segments, method),
            "teamlab" => ResolveTeamLab(segments, method),
            "runtime" => "runtime.inventory",
            _ => "unknown"
        };
    }

    private static string ResolveImage(string[] segments, HttpMethod method)
    {
        if (segments.Length < 3)
            return "image.unknown";
        return segments[2].ToLowerInvariant() switch
        {
            "pull-docker" => "image.docker.pull",
            "docker" when method == HttpMethod.Delete => "image.docker.delete",
            "download-vm" => "image.vm.download",
            "vm" when method == HttpMethod.Delete => "image.vm.delete",
            "ensure-docker-registry" => "image.registry.ensure",
            "configure-docker-registry" => "image.registry.configure",
            _ => "image.unknown"
        };
    }

    private static string ResolveVm(string[] segments, HttpMethod method)
    {
        if (segments.Length == 3 && segments[2].Equals("create", StringComparison.OrdinalIgnoreCase))
            return "vm.create";
        if (segments.Length >= 4 && segments[3].Equals("ip", StringComparison.OrdinalIgnoreCase))
            return "vm.ip.read";
        return method == HttpMethod.Delete ? "vm.destroy" : "vm.unknown";
    }

    private static string ResolveContainer(string[] segments, HttpMethod method)
    {
        if (segments.Length == 3 && segments[2].Equals("create", StringComparison.OrdinalIgnoreCase))
            return "container.create";
        if (segments.Length >= 4 && segments[2].Equals("networks", StringComparison.OrdinalIgnoreCase))
            return "container.network.delete";
        if (segments.Length >= 4 && segments[2].Equals("fabric", StringComparison.OrdinalIgnoreCase))
            return method == HttpMethod.Delete ? "fabric.network.delete" : "fabric.network.apply";
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
        return method == HttpMethod.Delete ? "container.destroy" : "container.unknown";
    }

    private static string ResolveTeamLab(string[] segments, HttpMethod method)
    {
        if (segments.Length < 3)
            return "teamlab.unknown";
        var action = string.Join('.', segments.Skip(2).Select(segment =>
            int.TryParse(segment, out _) ? "id" : segment.ToLowerInvariant()));
        return method == HttpMethod.Get && action == "status" ? "teamlab.status" : $"teamlab.{action}";
    }
}
