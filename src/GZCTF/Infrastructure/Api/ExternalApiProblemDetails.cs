using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using GZCTF.Modules.Audit.Application;

namespace GZCTF.Infrastructure.Api;

public sealed class ExternalApiProblemDetailsModel : ProblemDetails
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("traceId")]
    public string TraceId { get; init; } = string.Empty;
}

public static class ExternalApiProblemDetails
{
    public static ProblemDetails Create(
        HttpContext context,
        int statusCode,
        string code,
        string title,
        string? detail = null)
    {
        if (context.RequestServices is { } services)
            services.GetService<ExternalApiAuditContext>()?.SetErrorCode(code);
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
        return problem;
    }

    public static async Task WriteAsync(
        HttpContext context,
        int statusCode,
        string code,
        string title,
        string? detail = null,
        Action<IHeaderDictionary>? configureHeaders = null,
        Action<ProblemDetails>? configureProblem = null)
    {
        if (context.Response.HasStarted)
            throw new InvalidOperationException("Cannot write ProblemDetails after the response has started.");

        var problem = Create(context, statusCode, code, title, detail);
        configureProblem?.Invoke(problem);

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        configureHeaders?.Invoke(context.Response.Headers);
        await context.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: context.RequestAborted);
    }
}
