using System.Diagnostics;

namespace GZCTF.GuestSupervisor.Lifecycle;

public sealed class GuestRebootController
{
    public Task RequestAsync(CancellationToken cancellationToken)
    {
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("shutdown.exe", "/r /t 0 /d p:4:1")
            : new ProcessStartInfo("/bin/systemctl", "reboot")
            {
                UseShellExecute = false
            };
        startInfo.CreateNoWindow = true;
        startInfo.UseShellExecute = false;
        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("guest_reboot_command_start_failed");
        return Task.CompletedTask;
    }
}
