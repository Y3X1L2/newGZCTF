using GZCTF.Modules.TeamLab.Application;

namespace GZCTF.Modules.TeamLab;

public static class TeamLabModuleRegistration
{
    public static IServiceCollection AddTeamLabModule(this IServiceCollection services)
    {
        services.AddScoped<Infrastructure.Netlab.NetlabConnection>();
        services.AddScoped<Infrastructure.Netlab.NetlabClient>();
        services.AddScoped<Infrastructure.Netlab.NetlabProxy>();
        services.AddScoped<NetlabWorkspaceAccess>();
        services.AddHttpClient("Netlab", client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });
        services.AddHttpClient("NetlabStream", client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(15) });
        services.AddScoped<Modules.Runtime.Contracts.ITeamLabRecordReferenceQuery, Modules.Runtime.Application.TeamLabRecordReferenceQuery>();
        services.AddScoped<Modules.Runtime.Contracts.ITeamLabCreationHistory, Modules.Runtime.Application.TeamLabRecordReferenceQuery>();
        return services;
    }
}
