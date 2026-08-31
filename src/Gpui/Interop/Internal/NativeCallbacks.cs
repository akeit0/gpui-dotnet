using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gpui;
using Gpui.Interop.Internal.Session;

namespace Gpui.Interop.Internal;

internal static unsafe class NativeCallbacks
{
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static int Render(ulong sessionId, RenderArena* arena, uint* root)
    {
        try
        {
            if (
                arena == null
                || root == null
                || !NativeRegistry.Sessions.TryGetValue(sessionId, out var session)
            )
            {
                return -100;
            }

            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(session.SynchronizationContext);
            try
            {
                var element = session.RenderRoot(arena);
                *root = element.Node;
                return 0;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }
        catch (RenderArenaGrowthRequiredException)
        {
            return NativeConstants.RenderGrowRequired;
        }
        catch (Exception exception)
        {
            NativeRegistry.RecordFailure(sessionId, exception);
            return -101;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static int ListRenderRange(
        ulong sessionId,
        ulong rendererToken,
        uint start,
        uint count,
        RenderArena* arena,
        uint* root
    )
    {
        try
        {
            if (
                arena == null
                || root == null
                || count == 0
                || count > 512
                || !NativeRegistry.Sessions.TryGetValue(sessionId, out var session)
            )
            {
                return -105;
            }

            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(session.SynchronizationContext);
            try
            {
                var element = session.RenderListRange(rendererToken, start, count, arena);
                *root = element.Node;
                return 0;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }
        catch (RenderArenaGrowthRequiredException)
        {
            return NativeConstants.RenderGrowRequired;
        }
        catch (Exception exception)
        {
            NativeRegistry.RecordFailure(sessionId, exception);
            return -106;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static int Click(
        ulong sessionId,
        ulong eventToken,
        ulong eventPayload,
        NativeClickEvent* nativeEvent
    )
    {
        try
        {
            if (
                nativeEvent == null
                || !NativeRegistry.Sessions.TryGetValue(sessionId, out var session)
            )
            {
                return -110;
            }

            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(session.SynchronizationContext);
            try
            {
                session.DispatchClick(
                    eventToken,
                    new ClickEvent
                    {
                        Payload = eventPayload,
                        X = nativeEvent->x,
                        Y = nativeEvent->y,
                        Buttons = nativeEvent->buttons,
                        Modifiers = nativeEvent->modifiers,
                    }
                );
                return 0;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }
        catch (Exception exception)
        {
            NativeRegistry.RecordFailure(sessionId, exception);
            return -111;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static int ControlEvent(
        ulong sessionId,
        ulong eventToken,
        NativeControlEvent* nativeEvent
    )
    {
        try
        {
            if (
                nativeEvent == null
                || nativeEvent->reserved != 0
                || nativeEvent->reserved2 != 0
                || nativeEvent->data_length < 0
                || (nativeEvent->data_length != 0 && nativeEvent->data == null)
                || nativeEvent->kind
                    is < (ushort)InputEventKind.Changed
                        or > (ushort)InputEventKind.FocusChanged
                || (nativeEvent->flags & ~1u) != 0
                || !NativeRegistry.Sessions.TryGetValue(sessionId, out var session)
            )
            {
                return -112;
            }

            var value =
                nativeEvent->data_length == 0
                    ? Array.Empty<byte>()
                    : new ReadOnlySpan<byte>(nativeEvent->data, nativeEvent->data_length).ToArray();
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(session.SynchronizationContext);
            try
            {
                session.DispatchInput(
                    eventToken,
                    new InputEvent(
                        (InputEventKind)nativeEvent->kind,
                        value,
                        (nativeEvent->flags & 1) != 0,
                        nativeEvent->revision
                    )
                );
                return 0;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }
        catch (Exception exception)
        {
            NativeRegistry.RecordFailure(sessionId, exception);
            return -113;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static int ApplicationStarted(ulong applicationId)
    {
        try
        {
            if (!NativeRegistry.Applications.TryGetValue(applicationId, out var application))
            {
                return -120;
            }
            application.Start();
            return 0;
        }
        catch (Exception exception)
        {
            if (NativeRegistry.Applications.TryGetValue(applicationId, out var application))
            {
                application.RecordFailure(exception);
            }
            return -122;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static int WindowClosed(ulong applicationId, ulong windowId, int nativeStatus)
    {
        try
        {
            if (!NativeRegistry.Applications.TryGetValue(applicationId, out var application))
            {
                return -120;
            }
            return application.WindowClosed(windowId, nativeStatus);
        }
        catch (Exception exception)
        {
            if (NativeRegistry.Applications.TryGetValue(applicationId, out var application))
            {
                application.RecordFailure(exception);
            }
            return -121;
        }
    }
}
