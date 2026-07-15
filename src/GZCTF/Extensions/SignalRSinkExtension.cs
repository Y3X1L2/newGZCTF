using GZCTF.Hubs;
using GZCTF.Hubs.Clients;
using GZCTF.Models.Request.Admin;
using Microsoft.AspNetCore.SignalR;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace GZCTF.Extensions;

public static class SignalRSinkExtension
{
    extension(LoggerSinkConfiguration loggerConfiguration)
    {
        public LoggerConfiguration SignalR(IServiceProvider serviceProvider) =>
            loggerConfiguration.Sink(new SignalRSink(serviceProvider), LogEventLevel.Information);
    }
}

public class SignalRSink(IServiceProvider serviceProvider) : ILogEventSink
{
    private IHubContext<AdminHub, IAdminClient>? _hubContext;

    public void Emit(LogEvent logEvent)
    {
        _hubContext ??= serviceProvider.GetRequiredService<IHubContext<AdminHub, IAdminClient>>();

        try
        {
            _hubContext.Clients.All.ReceivedLog(
                LogMessageModel.FromLogModel(LogModelFactory.FromLogEvent(logEvent))).Wait();
        }
        catch
        {
            // Real-time delivery is best effort; the database sink remains the durable raw-log path.
        }
    }
}
