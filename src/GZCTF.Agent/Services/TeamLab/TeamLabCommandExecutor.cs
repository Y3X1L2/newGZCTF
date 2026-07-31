using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.TeamLab;

public sealed class TeamLabCommandExecutor(
    IOptions<AgentTeamLabConfig> options,
    TeamLabCommandRunner runner,
    ILogger<TeamLabCommandExecutor> logger)
{
    private readonly AgentTeamLabConfig _config = options.Value;

    public async Task<TeamLabDryRunResponse> ExecuteAsync(
        IReadOnlyList<string> commands,
        bool requestDryRun,
        CancellationToken token)
    {
        var commandArray = commands.ToArray();
        if (!_config.Enable)
            return new TeamLabDryRunResponse(true, true,
                "TeamLab network mutation is disabled on this WorkerNode. Command plan returned without execution.",
                commandArray);
        if (_config.DryRun || requestDryRun)
            return new TeamLabDryRunResponse(true, true,
                "TeamLab command plan returned without execution.", commandArray);

        foreach (var command in commandArray)
        {
            var result = await runner.RunAsync(command, null, token);
            if (!result.Success)
                return new TeamLabDryRunResponse(false, false, result.Output, commandArray);
        }

        logger.LogInformation("Executed {Count} TeamLab infrastructure commands.", commandArray.Length);
        return new TeamLabDryRunResponse(true, false, "Commands executed.", commandArray);
    }
}
