using System.Security.Cryptography;
using System.Text;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.GuestControl;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Middlewares;

public sealed class AgentEndpointAuthenticationMiddleware(
    RequestDelegate next,
    IOptions<AgentConfig> options,
    GuestCertificateAuthority certificateAuthority)
{
    private readonly AgentConfig _config = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        var guestRoute = context.Request.Path.StartsWithSegments("/api/guest", StringComparison.OrdinalIgnoreCase) &&
                         !context.Request.Path.StartsWithSegments("/api/guest-control",
                             StringComparison.OrdinalIgnoreCase);
        var managementListener = _config.GuestManagement.Enabled &&
                                 context.Connection.LocalPort == _config.GuestManagement.ListenPort;
        if (guestRoute != managementListener)
        {
            await RejectAsync(context, "auth.endpoint_mismatch");
            return;
        }

        if (managementListener)
        {
            if (string.Equals(context.Request.Path.Value, "/api/guest/v1/enroll",
                    StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }
            var certificate = await context.Connection.GetClientCertificateAsync(context.RequestAborted);
            if (certificate is null || !certificateAuthority.IsIssuedClientCertificate(certificate))
            {
                await RejectAsync(context, "auth.guest_certificate_invalid");
                return;
            }
            await next(context);
            return;
        }

        if (string.IsNullOrEmpty(_config.AuthToken) || _config.AuthToken == "__local__")
        {
            await RejectAsync(context, "auth.not_configured");
            return;
        }
        var header = context.Request.Headers.Authorization.ToString();
        var supplied = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header[7..].Trim()
            : string.Empty;
        if (!FixedEquals(supplied, _config.AuthToken))
        {
            await RejectAsync(context, "auth.forbidden");
            return;
        }
        await next(context);
    }

    private static bool FixedEquals(string supplied, string expected)
    {
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return suppliedBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }

    private static Task RejectAsync(HttpContext context, string code) =>
        AgentCorrelationErrorMiddleware.WriteAsync(context,
            new AgentErrorResponse(
                "Authorization",
                code,
                "Agent endpoint authentication failed.",
                false,
                $"{context.Request.Method.ToLowerInvariant()}.auth",
                context.Response.Headers[AgentProtocolHeaders.CorrelationId]!),
            StatusCodes.Status401Unauthorized,
            context.RequestAborted);
}
