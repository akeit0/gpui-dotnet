using System.Buffers.Binary;
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
            NativeRegistry.RecordRenderFailure(sessionId, exception);
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
            NativeRegistry.RecordRenderFailure(sessionId, exception);
            return -106;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static int DynamicFrame(ulong sessionId, uint ownerView)
    {
        try
        {
            if (ownerView == 0 || !NativeRegistry.Sessions.TryGetValue(sessionId, out var session))
            {
                return -107;
            }

            session.PrepareDynamicFrame(ownerView);
            return 0;
        }
        catch (Exception exception)
        {
            NativeRegistry.RecordFailure(sessionId, exception);
            return -108;
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
                || !NativeRegistry.Sessions.TryGetValue(sessionId, out var session)
            )
            {
                return -112;
            }

            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(session.SynchronizationContext);
            try
            {
                if ((nativeEvent->kind & 0x8000) != 0)
                {
                    var kind = checked((ushort)(nativeEvent->kind & 0x7FFF));
                    if (kind == 0)
                    {
                        return -112;
                    }

                    var payload =
                        nativeEvent->data_length == 0
                            ? Array.Empty<byte>()
                            : new ReadOnlySpan<byte>(
                                nativeEvent->data,
                                nativeEvent->data_length
                            ).ToArray();
                    session.DispatchNativeExtension(
                        eventToken,
                        new NativeExtensionEvent(
                            kind,
                            nativeEvent->flags,
                            nativeEvent->revision,
                            payload
                        )
                    );
                }
                else if (
                    nativeEvent->kind
                    is >= (ushort)InputEventKind.Changed
                        and <= (ushort)InputEventKind.FocusChanged
                )
                {
                    if ((nativeEvent->flags & ~1u) != 0)
                    {
                        return -112;
                    }

                    var value =
                        nativeEvent->data_length == 0
                            ? Array.Empty<byte>()
                            : new ReadOnlySpan<byte>(
                                nativeEvent->data,
                                nativeEvent->data_length
                            ).ToArray();
                    session.DispatchInput(
                        eventToken,
                        new InputEvent(
                            (InputEventKind)nativeEvent->kind,
                            value,
                            (nativeEvent->flags & 1) != 0,
                            nativeEvent->revision
                        )
                    );
                }
                else if (
                    nativeEvent->kind is (ushort)SliderEventKind.Changed
                        or (ushort)SliderEventKind.Released
                )
                {
                    if ((nativeEvent->flags & ~2u) != 0)
                    {
                        return -112;
                    }

                    var isRange = (nativeEvent->flags & 2) != 0;
                    var expectedLength = isRange ? 8 : 4;
                    if (nativeEvent->data_length != expectedLength)
                    {
                        return -112;
                    }

                    var data = new ReadOnlySpan<byte>(nativeEvent->data, nativeEvent->data_length);
                    var start = BitConverter.Int32BitsToSingle(
                        BinaryPrimitives.ReadInt32LittleEndian(data)
                    );
                    var end = isRange
                        ? BitConverter.Int32BitsToSingle(
                            BinaryPrimitives.ReadInt32LittleEndian(data[4..])
                        )
                        : start;
                    if (!float.IsFinite(start) || !float.IsFinite(end) || (isRange && start > end))
                    {
                        return -112;
                    }

                    session.DispatchSlider(
                        eventToken,
                        new SliderEvent(
                            nativeEvent->kind == (ushort)SliderEventKind.Changed
                                ? SliderEventKind.Changed
                                : SliderEventKind.Released,
                            start,
                            end,
                            isRange,
                            nativeEvent->revision
                        )
                    );
                }
                else if (
                    nativeEvent->kind is (ushort)DockEventKind.LayoutChanged
                        or (ushort)DockEventKind.LayoutExported
                        or (ushort)DockEventKind.PanelClosed
                )
                {
                    // Dock layout-changed carries no payload; layout-exported carries UTF-8
                    // JSON; panel-closed carries the UTF-8 panel id. Flags are reserved.
                    if (nativeEvent->flags != 0)
                    {
                        return -112;
                    }

                    var text =
                        nativeEvent->data_length == 0
                            ? string.Empty
                            : System.Text.Encoding.UTF8.GetString(
                                new ReadOnlySpan<byte>(
                                    nativeEvent->data,
                                    nativeEvent->data_length
                                )
                            );
                    var changedKind = (ushort)DockEventKind.LayoutChanged;
                    if (
                        (nativeEvent->kind == changedKind && text.Length != 0)
                        || (nativeEvent->kind != changedKind && text.Length == 0)
                    )
                    {
                        return -112;
                    }

                    session.DispatchDock(
                        eventToken,
                        new DockEvent(
                            (DockEventKind)nativeEvent->kind,
                            nativeEvent->kind == (ushort)DockEventKind.PanelClosed
                                ? text
                                : string.Empty,
                            nativeEvent->kind == (ushort)DockEventKind.LayoutExported
                                ? text
                                : string.Empty,
                            nativeEvent->revision
                        )
                    );
                }
                else
                {
                    return -112;
                }
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

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static int MenuAction(ulong applicationId, ulong actionId)
    {
        try
        {
            if (!NativeRegistry.Applications.TryGetValue(applicationId, out var application))
            {
                return -120;
            }
            return application.MenuAction(actionId);
        }
        catch (Exception exception)
        {
            if (NativeRegistry.Applications.TryGetValue(applicationId, out var application))
            {
                application.RecordFailure(exception);
            }
            return -123;
        }
    }
}
