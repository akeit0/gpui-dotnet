using System.Text;
using Gpui;

namespace Gpui.Interop.Internal.Session;

internal sealed unsafe partial class ManagedSession
{
    internal void DispatchNativeExtensionCommand(
        uint ownerView,
        uint schemaVersion,
        ulong schemaHash,
        ReadOnlySpan<byte> extensionId,
        ReadOnlySpan<byte> componentKind,
        ReadOnlySpan<byte> utf8Key,
        ushort command,
        ushort flags,
        ulong expectedRevision,
        ReadOnlySpan<byte> payload
    )
    {
        if (Volatile.Read(ref _stopped) != 0 || ownerView == 0)
        {
            return;
        }

        fixed (byte* extensionIdPointer = extensionId)
        fixed (byte* componentKindPointer = componentKind)
        fixed (byte* keyPointer = utf8Key)
        fixed (byte* payloadPointer = payload)
        {
            var native = new NativeExtensionCommand
            {
                owner_view = ownerView,
                command = command,
                flags = flags,
                schema_version = schemaVersion,
                reserved = 0,
                schema_hash = schemaHash,
                expected_revision = expectedRevision,
                extension_id = extensionIdPointer,
                extension_id_length = extensionId.Length,
                component_kind = componentKindPointer,
                component_kind_length = componentKind.Length,
                key = keyPointer,
                key_length = utf8Key.Length,
                payload = payloadPointer,
                payload_length = payload.Length,
            };
            var status = _runtime.Api->dispatch_extension_command(_sessionId, &native);
            if (status is -30 or -31)
            {
                return;
            }
            if (status != 0)
            {
                throw new InvalidOperationException(
                    $"Native extension command failed with status {status}."
                );
            }
        }
    }

    internal void DispatchResourceCommand(uint ownerView, ResourceCommand command)
    {
        if (Volatile.Read(ref _stopped) != 0 || ownerView == 0)
        {
            return;
        }
        if (command.Utf8Key is null && string.IsNullOrEmpty(command.Key))
        {
            throw new ArgumentException("A native resource key cannot be empty.", nameof(command));
        }

        var keyUtf8 = command.Utf8Key ?? Encoding.UTF8.GetBytes(command.Key!);
        var dataUtf8 = command.Data is null
            ? Array.Empty<byte>()
            : Encoding.UTF8.GetBytes(command.Data);
        fixed (byte* key = keyUtf8)
        fixed (byte* data = dataUtf8)
        {
            var native = new NativeResourceCommand
            {
                owner_view = ownerView,
                resource_kind = (ushort)command.ResourceKind,
                command = (ushort)command.Command,
                key = key,
                key_length = keyUtf8.Length,
                data = data,
                data_length = dataUtf8.Length,
                reserved = 0,
                a = command.A,
                b = command.B,
            };
            var status = _runtime.Api->dispatch_command(_sessionId, &native);
            if (status is -30 or -31)
            {
                return;
            }
            if (status != 0)
            {
                throw new InvalidOperationException(
                    $"Native resource command failed with status {status}."
                );
            }
        }
    }

    internal void DispatchUtf8InputValue(
        uint ownerView,
        ReadOnlySpan<byte> utf8Key,
        ReadOnlySpan<byte> utf8Value
    )
    {
        if (Volatile.Read(ref _stopped) != 0 || ownerView == 0)
        {
            return;
        }
        if (utf8Key.IsEmpty)
        {
            throw new ArgumentException("A native resource key cannot be empty.", nameof(utf8Key));
        }

        fixed (byte* key = utf8Key)
        fixed (byte* data = utf8Value)
        {
            var native = new NativeResourceCommand
            {
                owner_view = ownerView,
                resource_kind = (ushort)ResourceKind.Input,
                command = (ushort)ResourceCommandKind.InputSetValue,
                key = key,
                key_length = utf8Key.Length,
                data = data,
                data_length = utf8Value.Length,
                reserved = 0,
                a = 0,
                b = 0,
            };
            var status = _runtime.Api->dispatch_command(_sessionId, &native);
            if (status is -30 or -31)
            {
                return;
            }
            if (status != 0)
            {
                throw new InvalidOperationException(
                    $"Native input value command failed with status {status}."
                );
            }
        }
    }

    internal void DispatchClick(ulong eventToken, ClickEvent clickEvent)
    {
        var viewHandle = (uint)(eventToken >> 32);
        var handlerId = (uint)eventToken;
        if (viewHandle == 0 || handlerId == 0)
        {
            throw new InvalidOperationException("Malformed event token.");
        }
        if (!_viewsByHandle.TryGetValue(viewHandle, out var owner))
        {
            throw new InvalidOperationException(
                $"Event references unmounted or unknown view handle {viewHandle}."
            );
        }

        var pending = owner.DispatchClickCore(handlerId, clickEvent);
        if (!pending.IsCompletedSuccessfully)
        {
            ObserveEventTask(pending);
        }
    }

    internal void DispatchInput(ulong eventToken, InputEvent inputEvent)
    {
        var viewHandle = (uint)(eventToken >> 32);
        var handlerId = (uint)eventToken;
        if (viewHandle == 0 || handlerId == 0)
        {
            throw new InvalidOperationException("Malformed generated input event token.");
        }
        if (!_viewsByHandle.TryGetValue(viewHandle, out var owner))
        {
            throw new InvalidOperationException(
                $"Input event references unmounted or unknown view handle {viewHandle}."
            );
        }

        var pending = owner.DispatchInputCore(handlerId, inputEvent);
        if (!pending.IsCompletedSuccessfully)
        {
            ObserveEventTask(pending);
        }
    }

    private void ObserveEventTask(ValueTask pending)
    {
        _ = pending
            .AsTask()
            .ContinueWith(
                completed => ObserveEventCompletion(completed),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
    }

    private void ObserveEventCompletion(Task pending)
    {
        try
        {
            pending.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            RecordFailure(exception);
            Interlocked.CompareExchange(
                ref _pendingAsyncFailure,
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception),
                null
            );
            try
            {
                NotifyRenderPending();
            }
            catch (Exception notifyFailure)
            {
                RecordFailure(notifyFailure);
            }
        }
    }

    internal void DispatchSlider(ulong eventToken, SliderEvent sliderEvent)
    {
        var viewHandle = (uint)(eventToken >> 32);
        var handlerId = (uint)eventToken;
        if (viewHandle == 0 || handlerId == 0)
        {
            throw new InvalidOperationException("Malformed slider event token.");
        }
        if (!_viewsByHandle.TryGetValue(viewHandle, out var owner))
        {
            throw new InvalidOperationException(
                $"Slider event references unmounted or unknown view handle {viewHandle}."
            );
        }

        var pending = owner.DispatchSliderCore(handlerId, sliderEvent);
        if (!pending.IsCompletedSuccessfully)
        {
            ObserveEventTask(pending);
        }
    }

    internal void DispatchDock(ulong eventToken, DockEvent dockEvent)
    {
        var viewHandle = (uint)(eventToken >> 32);
        var handlerId = (uint)eventToken;
        if (viewHandle == 0 || handlerId == 0)
        {
            throw new InvalidOperationException("Malformed dock event token.");
        }
        if (!_viewsByHandle.TryGetValue(viewHandle, out var owner))
        {
            throw new InvalidOperationException(
                $"Dock event references unmounted or unknown view handle {viewHandle}."
            );
        }

        var pending = owner.DispatchDockCore(handlerId, dockEvent);
        if (!pending.IsCompletedSuccessfully)
        {
            ObserveEventTask(pending);
        }
    }

    internal void DispatchNativeExtension(
        ulong eventToken,
        NativeExtensionEvent nativeExtensionEvent
    )
    {
        var viewHandle = (uint)(eventToken >> 32);
        var handlerId = (uint)eventToken;
        if (viewHandle == 0 || handlerId == 0)
        {
            throw new InvalidOperationException("Malformed native extension event token.");
        }
        if (!_viewsByHandle.TryGetValue(viewHandle, out var owner))
        {
            throw new InvalidOperationException(
                $"Native extension event references unmounted or unknown view handle {viewHandle}."
            );
        }

        var pending = owner.DispatchNativeExtensionCore(handlerId, nativeExtensionEvent);
        if (!pending.IsCompletedSuccessfully)
        {
            ObserveEventTask(pending);
        }
    }
}
