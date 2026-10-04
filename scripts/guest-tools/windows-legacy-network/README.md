# Optional legacy Windows network script host

Use this component only for a Windows image whose stock `powershell.exe` ConsoleHost
fails under QGA (for example, Server 2008 R2 reporting
`ConsoleControl.GetActiveScreenBufferHandle`). It hosts the installed PowerShell
engine directly; it does not upgrade Windows, load a user profile, register a
service, change execution policy, or open a network listener.

The Agent looks for exactly `C:\Program Files\YINYU-GuestTools\LegacyPowerShellHost.exe`. When present,
Windows ManagedStatic read/apply/verify use this host for that operation. When it
is absent, the stock PowerShell command remains in use. Host execution failures
are failures; the Agent does not repeat a write through a second interpreter.

## Build once in the image preparation environment

Keep the original image and snapshot. Place `LegacyPowerShellHost.cs` and
`build-host.cmd` together, then run the command file from an administrator CMD.
The recipe targets the tested Server 2008 R2 .NET 2/PowerShell 2 SDK layout; other
Windows versions may have a different assembly location. Do not copy another
system's Windows assemblies into the image.

The component resides beneath the standard protected Program Files parent. Before
publishing, verify that unprivileged users cannot overwrite or replace the host,
the directory, or its parent. Keep any ACL backup with private preparation
evidence, not in Git. The normal platform instance access policy remains separate.

## Contract and validation

- No executable or script path arguments; only the platform-generated UTF-8
  network script is accepted on stdin, at most 64 KiB.
- Empty input returns 78 and `GZCTF_GUEST_STDIN_UNAVAILABLE` on stderr.
- Normal network scripts return raw UTF-8/XML and exit 0; a terminating exception,
  error stream, or checked native-command failure returns nonzero.
- Native network command failures must be checked by the generated script. This
  is not a general shell adapter, and it does not promise arbitrary `exit n`
  semantics or interactive commands.
- The Agent retains its normal command timeout, cancellation, VM identity checks
  and Windows process-tree cleanup.

Test the actual generated read script, successful native reads, empty stdin,
`throw`, `Write-Error`, native failure, and then an isolated platform instance's
apply/readback/reset/destruction. A successful QGA ping is insufficient.

The Server 2008 R2 image used to develop this component passed stdin, WMI IP/DNS,
the generated Agent read script (including Chinese adapter names), and the error
cases. Full scene application/reset requires the new Agent deployment and new
image import. VSS/COM+ backup registration is a separate unresolved image issue.

Commit source and instructions only. Compiled executables, images, credentials,
raw preparation logs and snapshots stay outside Git.
