namespace GZCTF.Models.Internal;

/// <summary>
/// Configuration for KVM/libvirt virtual machine management.
/// Maps to the "KvmSettings" section in appsettings.json.
/// </summary>
public class KvmSettings
{
    /// <summary>
    /// libvirt connection URI (e.g., qemu:///system)
    /// </summary>
    public string LibvirtUri { get; set; } = "qemu:///system";

    /// <summary>
    /// Base path for storing VM disk images
    /// </summary>
    public string ImageStoragePath { get; set; } = "/var/lib/gzctf/images";

    /// <summary>
    /// Default memory allocation per VM in MB
    /// </summary>
    public int DefaultVmMemoryMb { get; set; } = 2048;

    /// <summary>
    /// Default CPU core count per VM
    /// </summary>
    public int DefaultVmCpu { get; set; } = 2;

    /// <summary>
    /// Maximum time in seconds to wait for VM operations before timing out
    /// </summary>
    public int OperationTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Maximum upload size for VM disk images in GB
    /// </summary>
    public int MaxUploadSizeGb { get; set; } = 50;
}
