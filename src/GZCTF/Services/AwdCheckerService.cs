using System.Diagnostics;
using GZCTF.Models.Data;
using GZCTF.Repositories.Interface;

namespace GZCTF.Services;

public class AwdCheckerService(
    IAwdRepository awdRepository,
    ILogger<AwdCheckerService> logger)
{
    public async Task RunCheckerForRound(AwdRound round, AwdService[] services, List<Participation> participations)
    {
        var tasks = new List<AwdCheckerTask>();

        foreach (var service in services)
        {
            if (string.IsNullOrEmpty(service.CheckerScript))
                continue;

            var instances = await awdRepository.GetInstancesByGame(round.GameId);

            foreach (var part in participations)
            {
                var instance = instances.FirstOrDefault(i => i.ServiceId == service.Id && i.TeamId == part.TeamId);
                if (instance?.Container is null) continue;

                var flag = await awdRepository.GetFlag(round.Id, service.Id, part.TeamId);
                var status = await ExecuteChecker(service, instance, flag?.FlagValue ?? "");

                tasks.Add(new AwdCheckerTask
                {
                    RoundId = round.Id,
                    ServiceId = service.Id,
                    TeamId = part.TeamId,
                    Status = status.Status,
                    Message = status.Message
                });
            }
        }

        await awdRepository.CreateCheckerTasks(tasks);
    }

    private async Task<(CheckerStatus Status, string? Message)> ExecuteChecker(AwdService service, AwdServiceInstance instance, string flagValue)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"-c \"{service.CheckerScript}\" {instance.Container!.IP} {service.ExposePort} {flagValue}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return (CheckerStatus.Down, "Failed to start checker process");

            var timeout = Task.Delay(TimeSpan.FromSeconds(30));
            var completed = await Task.WhenAny(process.WaitForExitAsync(), timeout);

            if (completed == timeout)
            {
                process.Kill();
                return (CheckerStatus.Down, "Checker timeout");
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            output = output.Trim().ToUpperInvariant();

            return output switch
            {
                "OK" => (CheckerStatus.OK, null),
                "MUMBLE" => (CheckerStatus.Mumble, error),
                "DOWN" => (CheckerStatus.Down, error),
                "CORRUPT" => (CheckerStatus.Corrupt, error),
                _ => (CheckerStatus.Mumble, $"Unknown output: {output}")
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Checker execution failed for service {ServiceId}, team {TeamId}",
                service.Id, instance.TeamId);
            return (CheckerStatus.Down, ex.Message);
        }
    }
}
