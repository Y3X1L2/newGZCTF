using GZCTF.Modules.Audit.Application;

namespace GZCTF.Infrastructure.Api;

public sealed class ExternalApiExceptionHandler(
    RequestDelegate next,
    ILogger<ExternalApiExceptionHandler> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/open/v1", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        try
        {
            await next(context);
        }
        catch (ApiContractException exception)
        {
            logger.LogWarning(
                "External API request rejected: code={Code}, trace={TraceId}",
                exception.Code,
                context.TraceIdentifier);
            await ExternalApiProblemDetails.WriteAsync(
                context,
                exception.StatusCode,
                exception.Code,
                "The request could not be processed.",
                exception.Message);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled external API exception");
            await ExternalApiProblemDetails.WriteAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "An internal error occurred.");
        }
    }
}
