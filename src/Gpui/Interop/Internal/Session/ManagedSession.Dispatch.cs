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
        if (!IsAcceptingWork || ownerView == 0)
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
                    status == -34
                        ? "The extension resource is not declared in the accepted snapshot."
                        : $"Native extension command failed with status {status}."
                );
            }
        }
    }

    internal void DispatchResourceCommand(uint ownerView, ResourceCommand command)
    {
        if (!IsAcceptingWork || ownerView == 0)
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
                    status == -34
                        ? "The resource is not declared in the accepted snapshot."
                        : $"Native resource command failed with status {status}."
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
        if (!IsAcceptingWork || ownerView == 0)
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
                    status == -34
                        ? "The Input resource is not declared in the accepted snapshot."
                        : $"Native input value command failed with status {status}."
                );
            }
        }
    }

    internal void DispatchClick(ulong eventToken, ClickEvent value) =>
        DispatchEvent(eventToken, value, static (owner, id, data) => owner.DispatchClickCore(id, data));

    internal void DispatchInput(ulong eventToken, InputEvent value) =>
        DispatchEvent(eventToken, value, static (owner, id, data) => owner.DispatchInputCore(id, data));

    internal void DispatchSlider(ulong eventToken, SliderEvent value) =>
        DispatchEvent(eventToken, value, static (owner, id, data) => owner.DispatchSliderCore(id, data));

    internal void DispatchDock(ulong eventToken, DockEvent value) =>
        DispatchEvent(eventToken, value, static (owner, id, data) => owner.DispatchDockCore(id, data));

    internal void DispatchKey(ulong eventToken, KeyEvent value) =>
        DispatchEvent(eventToken, value, static (owner, id, data) => owner.DispatchKeyCore(id, data));

    internal void DispatchMouse(ulong eventToken, MouseEvent value) =>
        DispatchEvent(eventToken, value, static (owner, id, data) => owner.DispatchMouseCore(id, data));

    internal void DispatchModifiers(ulong eventToken, ModifiersEvent value) =>
        DispatchEvent(eventToken, value, static (owner, id, data) => owner.DispatchModifiersCore(id, data));

    internal void DispatchHover(ulong eventToken, HoverEvent value) =>
        DispatchEvent(eventToken, value, static (owner, id, data) => owner.DispatchHoverCore(id, data));

    internal void DispatchMouseMove(ulong eventToken, MouseMoveEvent value) =>
        DispatchEvent(eventToken, value, static (owner, id, data) => owner.DispatchMouseMoveCore(id, data));

    internal void DispatchScrollWheel(ulong eventToken, ScrollWheelEvent value) =>
        DispatchEvent(eventToken, value, static (owner, id, data) => owner.DispatchScrollWheelCore(id, data));

    internal void DispatchFileDrop(ulong eventToken, FileDropEvent value) =>
        DispatchEvent(eventToken, value, static (owner, id, data) => owner.DispatchFileDropCore(id, data));

    internal void DispatchNativeExtension(ulong eventToken, NativeExtensionEvent value) =>
        DispatchEvent(eventToken, value, static (owner, id, data) => owner.DispatchNativeExtensionCore(id, data));

    private void DispatchEvent<TEvent>(
        ulong eventToken,
        TEvent value,
        Func<ViewBase, uint, TEvent, ValueTask> dispatch
    )
    {
        ThrowIfUnavailable();
        using var execution = Execution.Enter(ExecutionPhase.Event);
        RequireAcceptedRender();
        try
        {
            var viewHandle = (uint)(eventToken >> 32);
            var handlerId = (uint)eventToken;
            if (viewHandle == 0 || !ViewBase.IsWellFormedEventId(handlerId))
            {
                throw new InvalidOperationException("Malformed event token.");
            }
            if (!_viewsByHandle.TryGetValue(viewHandle, out var owner))
            {
                if (viewHandle <= _nextViewHandle)
                {
                    return;
                }
                throw new InvalidOperationException(
                    $"Event references unmounted or unknown view handle {viewHandle}."
                );
            }

            var pending = dispatch(owner, handlerId, value);
            if (pending.IsCompletedSuccessfully)
            {
                pending.GetAwaiter().GetResult();
            }
            else if (pending.IsCompleted)
            {
                // Preserve cancellation as a normal outcome for the legacy async event API.
                try
                {
                    pending.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) { }
            }
            else
            {
                ObserveEventTask(pending);
            }
            ThrowIfUnavailable();
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
    }

    private void ObserveEventTask(ValueTask pending)
    {
        _ = pending.AsTask().ContinueWith(
            static (completed, state) => ((ManagedSession)state!).ObserveEventCompletion(completed),
            this,
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
}
