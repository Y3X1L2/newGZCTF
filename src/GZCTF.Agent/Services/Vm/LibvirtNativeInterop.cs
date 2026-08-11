using System.Runtime.InteropServices;
using System.Text;
using GZCTF.Agent.Models;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.Vm;

internal static partial class LibvirtNativeInterop
{
    internal const int DomainEventIdLifecycle = 0;
    internal const int DomainEventDefined = 0;
    internal const int DomainEventStarted = 2;
    internal const int DomainEventSuspended = 3;
    internal const int DomainEventStopped = 5;
    internal const int DomainEventResumed = 7;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void DomainLifecycleCallback(nint connection, nint domain, int eventId, int detail, nint opaque);

    [DllImport("libvirt.so.0", EntryPoint = "virConnectOpen", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint ConnectOpen([MarshalAs(UnmanagedType.LPStr)] string uri);

    [DllImport("libvirt.so.0", EntryPoint = "virConnectClose", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ConnectClose(nint connection);

    [DllImport("libvirt.so.0", EntryPoint = "virEventRegisterDefaultImpl", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int EventRegisterDefaultImpl();

    [DllImport("libvirt.so.0", EntryPoint = "virEventRunDefaultImpl", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int EventRunDefaultImpl();

    [DllImport("libvirt.so.0", EntryPoint = "virConnectDomainEventRegisterAny", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int RegisterDomainEvent(
        nint connection,
        nint domain,
        int eventId,
        DomainLifecycleCallback callback,
        nint opaque,
        nint freeCallback);

    [DllImport("libvirt.so.0", EntryPoint = "virDomainLookupByName", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint DomainLookupByName(
        nint connection,
        [MarshalAs(UnmanagedType.LPStr)] string name);

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
}

public sealed class LibvirtEventDispatcher(
    ILogger<LibvirtEventDispatcher> logger,
    IOptions<KvmConfig> options) : IHostedService, IDisposable
{
    readonly object sync = new();
    LibvirtConnection? connection;
    Thread? eventThread;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (eventThread is not null) return Task.CompletedTask;
            try
            {
                connection = LibvirtConnection.TryOpen(logger, options.Value.LibvirtUri);
                if (connection is null) return Task.CompletedTask;
                LibvirtNativeInterop.EventRegisterDefaultImpl();
                connection.RegisterLifecycleEvents();
                eventThread = new Thread(RunEventLoop)
                {
                    IsBackground = true,
                    Name = "gzctf-libvirt-events"
                };
                eventThread.Start();
            }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
            {
                logger.LogInformation(exception, "Native libvirt events are unavailable on this Agent.");
                connection?.Dispose();
                connection = null;
            }
        }
        return Task.CompletedTask;
    }

    void RunEventLoop()
    {
        while (connection is not null)
        {
            try
            {
                if (LibvirtNativeInterop.EventRunDefaultImpl() < 0) break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Native libvirt event loop stopped.");
                break;
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose()
    {
        lock (sync)
        {
            connection?.Dispose();
            connection = null;
        }
    }
}

public sealed class LibvirtConnection : IDisposable
{
    readonly nint handle;
    LibvirtNativeInterop.DomainLifecycleCallback? callback;

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

    public nint Define(string xml) => LibvirtNativeInterop.DomainDefineXml(handle, xml);

    public void RegisterLifecycleEvents()
    {
        callback = static (_, domain, eventId, _, _) =>
        {
            if (domain != 0)
                LibvirtNativeInterop.DomainFree(domain);
        };
        if (LibvirtNativeInterop.RegisterDomainEvent(
                handle, 0, LibvirtNativeInterop.DomainEventIdLifecycle, callback, 0, 0) < 0)
            throw new InvalidOperationException("libvirt lifecycle event registration failed.");
    }

    public void Dispose()
    {
        if (handle != 0) LibvirtNativeInterop.ConnectClose(handle);
        callback = null;
    }
}
