using System.Runtime.InteropServices;
using System.Text;

namespace GZCTF.Agent.Services.Vm;

internal static partial class LibvirtNativeInterop
{
    [DllImport("libvirt.so.0", EntryPoint = "virConnectOpen", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint ConnectOpen([MarshalAs(UnmanagedType.LPStr)] string uri);

    [DllImport("libvirt.so.0", EntryPoint = "virConnectClose", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ConnectClose(nint connection);

    [DllImport("libvirt.so.0", EntryPoint = "virDomainLookupByName", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint DomainLookupByName(
        nint connection,
        [MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport("libvirt.so.0", EntryPoint = "virConnectListAllDomains", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ConnectListAllDomains(nint connection, out nint domains, uint flags);

    [DllImport("libvirt.so.0", EntryPoint = "virDomainGetName", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint DomainGetName(nint domain);

    [DllImport("libvirt.so.0", EntryPoint = "virDomainDefineXML", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint DomainDefineXml(
        nint connection,
        [MarshalAs(UnmanagedType.LPStr)] string xml);

    [DllImport("libvirt.so.0", EntryPoint = "virDomainCreate", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int DomainCreate(nint domain);

    [DllImport("libvirt.so.0", EntryPoint = "virDomainSuspend", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int DomainSuspend(nint domain);

    [DllImport("libvirt.so.0", EntryPoint = "virDomainResume", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int DomainResume(nint domain);

    [DllImport("libvirt.so.0", EntryPoint = "virDomainDestroy", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int DomainDestroy(nint domain);

    [DllImport("libvirt.so.0", EntryPoint = "virDomainUndefineFlags", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int DomainUndefineFlags(nint domain, uint flags);

    [DllImport("libvirt.so.0", EntryPoint = "virDomainFree", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int DomainFree(nint domain);

    [DllImport("libvirt.so.0", EntryPoint = "virDomainGetState", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int DomainGetState(nint domain, out int state, out int reason, uint flags);

    [DllImport("libvirt.so.0", EntryPoint = "virDomainGetUUIDString", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int DomainGetUuidString(nint domain, StringBuilder uuid);

    [DllImport("libvirt.so.0", EntryPoint = "virDomainGetXMLDesc", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint DomainGetXmlDesc(nint domain, uint flags);

    // libvirt returns malloc-allocated buffers for the domain list and XML; virFree
    // does not exist, so libc free is the correct release for both.
    [DllImport("libc", EntryPoint = "free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Free(nint pointer);
}

public sealed class LibvirtConnection : IDisposable
{
    readonly nint handle;

    LibvirtConnection(nint handle) => this.handle = handle;

    public static LibvirtConnection? TryOpen(ILogger logger, string uri)
    {
        try
        {
            var handle = LibvirtNativeInterop.ConnectOpen(uri);
            if (handle == 0)
            {
                logger.LogInformation("libvirt returned no system connection.");
                return null;
            }
            return new LibvirtConnection(handle);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
    }

    public nint Lookup(string name) => LibvirtNativeInterop.DomainLookupByName(handle, name);

    public nint[] ListDomains()
    {
        var count = LibvirtNativeInterop.ConnectListAllDomains(handle, out var domains, 0);
        if (count < 0)
            throw new InvalidOperationException("libvirt failed to list domains.");
        try
        {
            var result = new nint[count];
            for (var index = 0; index < count; index++)
                result[index] = Marshal.ReadIntPtr(domains, index * IntPtr.Size);
            return result;
        }
        finally { LibvirtNativeInterop.Free(domains); }
    }

    public string? GetName(nint domain)
    {
        var name = LibvirtNativeInterop.DomainGetName(domain);
        return name == 0 ? null : Marshal.PtrToStringAnsi(name);
    }

    public nint Define(string xml) => LibvirtNativeInterop.DomainDefineXml(handle, xml);

    public string? GetXml(nint domain)
    {
        var xml = LibvirtNativeInterop.DomainGetXmlDesc(domain, 0);
        if (xml == 0) return null;
        try { return Marshal.PtrToStringAnsi(xml); }
        finally { LibvirtNativeInterop.Free(xml); }
    }

    public void Dispose()
    {
        if (handle != 0) LibvirtNativeInterop.ConnectClose(handle);
    }
}
