using GZCTF.Modules.Audit.Application;

namespace GZCTF.Infrastructure.Api;

/// <summary>
/// Preserves explicit application contract failures for every HTTP API surface.
/// Browser-facing administrative APIs share the same typed failures as OpenAPI;
/// only the OpenAPI audit middleware adds external-request audit context.
/// </summary>
public sealed class ExternalApiExceptionHandler(
    RequestDelegate next,
    ILogger<ExternalApiExceptionHandler> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Abort();
        }
        catch (Exception exception) when (context.Response.HasStarted)
        {
            logger.LogWarning(exception, "API stream closed after response started");
            context.Abort();
        }
        catch (BadHttpRequestException exception)
        {
            await ExternalApiProblemDetails.WriteAsync(context, exception.StatusCode,
                "invalid_request", "The request could not be processed.", exception.Message);
        }
        catch (ApiContractException exception)
        {
            await ExternalApiProblemDetails.WriteAsync(
                context,
                exception.StatusCode,
                exception.Code,
                "The request could not be processed.",
                exception.Message);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled API exception");
            await ExternalApiProblemDetails.WriteAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "An internal error occurred.");
        }
    }
}
