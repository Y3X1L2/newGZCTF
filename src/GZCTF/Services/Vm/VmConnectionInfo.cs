namespace GZCTF.Services.Vm;

/// <summary>
/// Connection information for a running VM.
/// </summary>
public class VmConnectionInfo
{
    public string? IP { get; init; }
    public int? VncPort { get; init; }
    public int? RdpPort { get; init; }
    public string? SshHost { get; init; }
    public int? SshPort { get; init; }
    public string Protocol { get; init; } = "vnc"; // "rdp", "vnc", "ssh"
}
