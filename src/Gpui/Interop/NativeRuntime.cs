using System.Runtime.InteropServices;
using Gpui;
using Gpui.Interop.Internal;

namespace Gpui.Interop;

/// <summary>
/// Control-plane wrapper around the versioned native function table. Render operations remain
/// direct arena writes and event dispatch uses generated numeric tokens rather than delegates.
/// </summary>
public sealed unsafe class NativeRuntime
{
    private const string ApiExportName = "gpui_dotnet_get_api";

    private readonly GpuiDotnetApiV1* _api;
    private readonly NativeLibraryHandle? _libraryHandle;

    private NativeRuntime(GpuiDotnetApiV1* api, NativeLibraryHandle? libraryHandle)
    {
        _api = api;
        _libraryHandle = libraryHandle;
    }

    internal GpuiDotnetApiV1* Api
    {
        get
        {
            GC.KeepAlive(_libraryHandle);
            return _api;
        }
    }

    /// <summary>Loads and validates the package's default native host.</summary>
    public static NativeRuntime Load() => Load(null);

    /// <summary>Loads and validates the default or explicitly selected native host.</summary>
    public static NativeRuntime Load(NativeRuntimeOptions? options)
    {
        if (options is null || options.LibraryPath is null)
        {
            return CreateValidated(NativeMethods.gpui_dotnet_get_api(NativeConstants.AbiVersion));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(options.LibraryPath);

        NativeLibraryHandle? libraryHandle = null;
        try
        {
            libraryHandle = NativeLibraryHandle.Load(options.LibraryPath);
            var export = NativeLibrary.GetExport(libraryHandle.DangerousGetHandle(), ApiExportName);
            var getApi = (delegate* unmanaged[Cdecl]<uint, GpuiDotnetApiV1*>)export;
            return CreateValidated(getApi(NativeConstants.AbiVersion), libraryHandle);
        }
        catch
        {
            libraryHandle?.Dispose();
            throw;
        }
    }

    private static NativeRuntime CreateValidated(
        GpuiDotnetApiV1* api,
        NativeLibraryHandle? libraryHandle = null
    )
    {
        if (api == null)
        {
            throw new InvalidOperationException(
                $"gpui-dotnet native ABI {NativeConstants.AbiVersion} is not supported."
            );
        }

        if (api->struct_size < (uint)sizeof(GpuiDotnetApiV1))
        {
            throw new InvalidOperationException(
                $"Native API table is too small. Managed={sizeof(GpuiDotnetApiV1)}, native={api->struct_size}."
            );
        }

        if (api->abi_version != NativeConstants.AbiVersion)
        {
            throw new InvalidOperationException(
                $"Native ABI mismatch. Managed={NativeConstants.AbiVersion}, native={api->abi_version}."
            );
        }

        if (api->schema_hash != NativeConstants.SchemaHash)
        {
            throw new InvalidOperationException(
                $"Binding schema mismatch. Managed=0x{NativeConstants.SchemaHash:X16}, native=0x{api->schema_hash:X16}."
            );
        }

        if (
            api->validate_render == null
            || api->run_application == null
            || api->notify_view == null
            || api->dispatch_command == null
            || api->dispatch_application_command == null
            || api->dispatch_application_menu == null
        )
        {
            throw new InvalidOperationException(
                "The native API table is missing required entries."
            );
        }

        return new NativeRuntime(api, libraryHandle);
    }

    public void Validate(RenderArenaOwner owner, Element root)
    {
        ArgumentNullException.ThrowIfNull(owner);
        // Without this check, a root element from another arena could validate an unrelated
        // node index in the owner's arena.
        if (root.Arena != owner.NativeArena || root.Generation != owner.NativeArena->Generation)
        {
            throw new InvalidOperationException(
                "Root does not belong to the owner's active render generation."
            );
        }
        var status = _api->validate_render(owner.NativeArena, root.Node);
        if (status != 0)
        {
            throw new InvalidOperationException(
                $"Native render validation failed with status {status}."
            );
        }
    }

    internal void Run(GpuiApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        var id = checked((ulong)Interlocked.Increment(ref NativeRegistry.NextApplicationId));
        var managedApplication = new ManagedApplication(this, id, application);
        if (!NativeRegistry.Applications.TryAdd(id, managedApplication))
        {
            throw new InvalidOperationException("Failed to register the managed GPUI application.");
        }

        var status = 0;
        try
        {
            var callbacks = new ManagedCallbacks
            {
                struct_size = (uint)sizeof(ManagedCallbacks),
                render = &NativeCallbacks.Render,
                click = &NativeCallbacks.Click,
                list_render_range = &NativeCallbacks.ListRenderRange,
                dynamic_frame = &NativeCallbacks.DynamicFrame,
                control_event = &NativeCallbacks.ControlEvent,
                application_started = &NativeCallbacks.ApplicationStarted,
                window_closed = &NativeCallbacks.WindowClosed,
                menu_action = &NativeCallbacks.MenuAction,
            };
            status = _api->run_application(id, &callbacks);
        }
        finally
        {
            managedApplication.Stop();
            NativeRegistry.Applications.TryRemove(id, out _);
        }

        if (status != 0)
        {
            throw new InvalidOperationException(
                $"The native GPUI application stopped with status {status}.",
                managedApplication.Failure
            );
        }

        if (managedApplication.Failure is not null)
        {
            throw new InvalidOperationException(
                "The managed GPUI application observed a window event, render, or lifecycle failure.",
                managedApplication.Failure
            );
        }
    }

    internal void NotifyView(ulong sessionId)
    {
        var status = _api->notify_view(sessionId);
        if (status is -30 or -31)
        {
            // A late continuation may race normal native teardown.
            return;
        }
        if (status != 0)
        {
            throw new InvalidOperationException(
                $"Native view notification failed with status {status}."
            );
        }
    }

    private sealed class NativeLibraryHandle : SafeHandle
    {
        private NativeLibraryHandle(nint handle)
            : base(handle, ownsHandle: true) { }

        public override bool IsInvalid => handle is 0 or -1;

        public static NativeLibraryHandle Load(string path) => new(NativeLibrary.Load(path));

        protected override bool ReleaseHandle()
        {
            NativeLibrary.Free(handle);
            return true;
        }
    }
}
