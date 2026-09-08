using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gpui;
using Gpui.Interop.Internal.Session;

namespace Gpui.Interop.Internal;

internal static unsafe class NativeCallbacks
{
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static int Render(ulong sessionId, RenderArena* arena, uint* root, ulong* revision)
    {
        try
        {
            if (
                arena == null
                || root == null
                || revision == null
                || !NativeRegistry.Sessions.TryGetValue(sessionId, out var session)
            )
            {
                return -100;
            }

            var previousContext = SynchronizationContext.Current;
            *revision = 0;
            SynchronizationContext.SetSynchronizationContext(session.SynchronizationContext);
            try
            {
                *root = session.RenderRootOutput(arena);
                *revision = session.PendingRenderRevision;
                return 0;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }
        catch (Exception exception)
        {
            NativeRegistry.RecordRenderFailure(sessionId, exception);
            return -101;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static int RenderCompleted(ulong sessionId, ulong revision, int status)
    {
        try
        {
            if (!NativeRegistry.Sessions.TryGetValue(sessionId, out var session))
            {
                return -102;
            }
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(session.SynchronizationContext);
            try
            {
                session.CompleteRender(revision, status);
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
            return -103;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static int ListRenderRange(
        ulong sessionId,
        ulong rendererToken,
        ulong source,
        uint start,
        uint count,
        RenderArena* arena,
        uint* root,
        ulong* artifact
    )
    {
        try
        {
            if (
                arena == null
                || root == null
                || artifact == null
                || source == 0
                || count == 0
                || count > 512
                || !NativeRegistry.Sessions.TryGetValue(sessionId, out var session)
            )
            {
                return -105;
            }

            var previousContext = SynchronizationContext.Current;
            *artifact = 0;
            SynchronizationContext.SetSynchronizationContext(session.SynchronizationContext);
            try
            {
                *root = session.RenderListRangeOutput(rendererToken, source, start, count, arena, out var artifactId);
                *artifact = artifactId;
                return 0;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }
        catch (Exception exception)
        {
            NativeRegistry.RecordRenderFailure(sessionId, exception);
            return -106;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static int ReleaseArtifact(ulong sessionId, ulong source, ulong artifact, int status)
    {
        try
        {
            if (NativeRegistry.Sessions.TryGetValue(sessionId, out var session))
            {
                session.ReleaseDemandArtifact(source, artifact, status);
            }
            return 0;
        }
        catch (Exception exception)
        {
            NativeRegistry.RecordFailure(sessionId, exception, deferCleanup: true);
            return -109;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static int AcceptArtifact(ulong sessionId, ulong source, ulong artifact)
    {
        try
        {
            if (!NativeRegistry.Sessions.TryGetValue(sessionId, out var session))
                return -109;
            session.AcceptDemandArtifact(source, artifact);
            return 0;
        }
        catch (Exception exception)
        {
            NativeRegistry.RecordFailure(sessionId, exception, deferCleanup: true);
            return -109;
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

                    var bytes = new ReadOnlySpan<byte>(nativeEvent->data, nativeEvent->data_length);
                    if (!IsValidUtf8(bytes))
                        return -112;
                    var value = bytes.IsEmpty ? Array.Empty<byte>() : bytes.ToArray();
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
                else if (nativeEvent->kind == (ushort)InputWriteEventKind.Completed)
                {
                    if (nativeEvent->flags != 0 || nativeEvent->revision == 0 || nativeEvent->data_length != 16)
                        return -112;
                    var data = new ReadOnlySpan<byte>(nativeEvent->data, 16);
                    var requestId = BinaryPrimitives.ReadUInt64LittleEndian(data);
                    var outcome = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
                    if (requestId == 0 || requestId > (ulong.MaxValue >> 2)
                        || outcome > (uint)InputWriteOutcome.Composing
                        || BinaryPrimitives.ReadUInt32LittleEndian(data[12..]) != 0)
                        return -112;
                    session.DispatchInputWrite(eventToken,
                        new InputWriteResult(requestId, (InputWriteOutcome)outcome, nativeEvent->revision));
                }
                else if (nativeEvent->kind is (ushort)ListEventKind.ContextMenuRequested or (ushort)ListEventKind.TooltipRequested)
                {
                    if ((nativeEvent->flags & ~2u) != 0 || nativeEvent->data_length != 24
                        || ((nativeEvent->flags & 2) == 0 && nativeEvent->revision != 0))
                        return -112;
                    var data = new ReadOnlySpan<byte>(nativeEvent->data, 24);
                    var index = BinaryPrimitives.ReadUInt32LittleEndian(data);
                    var itemId = BinaryPrimitives.ReadUInt64LittleEndian(data[8..]);
                    var anchorId = BinaryPrimitives.ReadUInt64LittleEndian(data[16..]);
                    if (index > int.MaxValue || BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) != 0
                        || itemId == 0 || anchorId == 0)
                        return -112;
                    if (nativeEvent->kind == (ushort)ListEventKind.TooltipRequested)
                        session.DispatchListTooltip(eventToken, new ListTooltipEvent(
                            (int)index, itemId, (nativeEvent->flags & 2) == 0 ? null : nativeEvent->revision, anchorId));
                    else
                        session.DispatchListContextMenu(eventToken, new ListContextMenuEvent(
                            (int)index, itemId, (nativeEvent->flags & 2) == 0 ? null : nativeEvent->revision, anchorId));
                }
                else if (nativeEvent->kind is (ushort)ListEventKind.Activated or (ushort)ListEventKind.SelectionRequested)
                {
                    if ((nativeEvent->flags & ~3u) != 0 || nativeEvent->data_length != 16
                        || ((nativeEvent->flags & 2) == 0 && nativeEvent->revision != 0))
                        return -112;
                    var data = new ReadOnlySpan<byte>(nativeEvent->data, 16);
                    var index = BinaryPrimitives.ReadUInt32LittleEndian(data);
                    if (index > int.MaxValue || BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) != 0)
                        return -112;
                    var itemId = BinaryPrimitives.ReadUInt64LittleEndian(data[8..]);
                    ulong? identity = itemId == 0 ? null : itemId;
                    ulong? revision = (nativeEvent->flags & 2) == 0 ? null : nativeEvent->revision;
                    var keyboard = (nativeEvent->flags & 1) != 0;
                    if (nativeEvent->kind == (ushort)ListEventKind.Activated)
                        session.DispatchListActivation(eventToken, new ListActivationEvent(
                            (int)index, identity, revision,
                            keyboard ? ListActivationSource.Keyboard : ListActivationSource.Pointer
                        ));
                    else
                        session.DispatchListSelection(eventToken, new ListSelectionEvent(
                            (int)index, identity, revision,
                            keyboard ? ListSelectionSource.Keyboard : ListSelectionSource.Pointer
                        ));
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
                    nativeEvent->kind is (ushort)KeyEventKind.Down or (ushort)KeyEventKind.Up
                )
                {
                    // Key observer events: data is the UTF-8 GPUI key name (non-empty,
                    // NUL-free, bounded); flags carry modifiers in bits 0-4 and the
                    // held-repeat bit 5 for key-down only; revision is reserved zero.
                    var isDown = nativeEvent->kind == (ushort)KeyEventKind.Down;
                    var allowedFlags = isDown ? ~0x3Fu : ~0x1Fu;
                    if (
                        (nativeEvent->flags & allowedFlags) != 0
                        || nativeEvent->revision != 0
                        || nativeEvent->data_length <= 0
                        || nativeEvent->data_length > 128
                    )
                    {
                        return -112;
                    }

                    var bytes = new ReadOnlySpan<byte>(nativeEvent->data, nativeEvent->data_length);
                    if (bytes.Contains((byte)0))
                    {
                        return -112;
                    }

                    if (!TryDecodeUtf8(bytes, out var key))
                    {
                        return -112;
                    }

                    var modifiers = (uint)(nativeEvent->flags & 0x1F);
                    var isHeld = isDown && (nativeEvent->flags & 0x20) != 0;
                    session.DispatchKey(
                        eventToken,
                        new KeyEvent(
                            (KeyEventKind)nativeEvent->kind,
                            key,
                            modifiers,
                            isHeld
                        )
                    );
                }
                else if (
                    nativeEvent->kind is (ushort)MouseEventKind.Down or (ushort)MouseEventKind.Up
                )
                {
                    // Mouse observer events: flags carry modifiers in bits 0-4;
                    // data is 16 bytes LE: f32 x, f32 y, u32 button, u32 click count.
                    if (
                        (nativeEvent->flags & ~0x1Fu) != 0
                        || nativeEvent->revision != 0
                        || nativeEvent->data_length != 16
                    )
                    {
                        return -112;
                    }

                    var data = new ReadOnlySpan<byte>(nativeEvent->data, 16);
                    var x = BitConverter.Int32BitsToSingle(
                        BinaryPrimitives.ReadInt32LittleEndian(data)
                    );
                    var y = BitConverter.Int32BitsToSingle(
                        BinaryPrimitives.ReadInt32LittleEndian(data[4..])
                    );
                    var button = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
                    var clickCount = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
                    if (
                        !float.IsFinite(x)
                        || !float.IsFinite(y)
                        || button > (uint)MouseButton.Forward
                        || clickCount > 255
                    )
                    {
                        return -112;
                    }

                    session.DispatchMouse(
                        eventToken,
                        new MouseEvent(
                            (MouseEventKind)nativeEvent->kind,
                            x,
                            y,
                            (MouseButton)button,
                            clickCount,
                            nativeEvent->flags
                        )
                    );
                }
                else if (nativeEvent->kind == (ushort)ModifiersEventKind.Changed)
                {
                    // Modifier-only presses never produce key events in GPUI; flags carry
                    // the current modifiers in bits 0-4 with no payload; revision is zero.
                    if (
                        (nativeEvent->flags & ~0x1Fu) != 0
                        || nativeEvent->revision != 0
                        || nativeEvent->data_length != 0
                    )
                    {
                        return -112;
                    }

                    session.DispatchModifiers(
                        eventToken,
                        new ModifiersEvent(nativeEvent->flags)
                    );
                }
                else if (nativeEvent->kind == (ushort)HoverEventKind.Changed)
                {
                    // Hover transitions only: flags carry modifiers in bits 0-4 and
                    // the hovering state in bit 5; no payload; revision is zero.
                    if (
                        (nativeEvent->flags & ~0x3Fu) != 0
                        || nativeEvent->revision != 0
                        || nativeEvent->data_length != 0
                    )
                    {
                        return -112;
                    }

                    session.DispatchHover(
                        eventToken,
                        new HoverEvent((nativeEvent->flags & 0x20) != 0)
                    );
                }
                else if (
                    nativeEvent->kind is (ushort)MouseEventKind.DownOut or (ushort)MouseEventKind.UpOut
                )
                {
                    // Outside press/release: same 16-byte LE payload as down/up.
                    if (
                        (nativeEvent->flags & ~0x1Fu) != 0
                        || nativeEvent->revision != 0
                        || nativeEvent->data_length != 16
                    )
                    {
                        return -112;
                    }

                    var data = new ReadOnlySpan<byte>(nativeEvent->data, 16);
                    var x = BitConverter.Int32BitsToSingle(
                        BinaryPrimitives.ReadInt32LittleEndian(data)
                    );
                    var y = BitConverter.Int32BitsToSingle(
                        BinaryPrimitives.ReadInt32LittleEndian(data[4..])
                    );
                    var button = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
                    var clickCount = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
                    if (
                        !float.IsFinite(x)
                        || !float.IsFinite(y)
                        || button > (uint)MouseButton.Forward
                        || clickCount > 255
                    )
                    {
                        return -112;
                    }

                    session.DispatchMouse(
                        eventToken,
                        new MouseEvent(
                            (MouseEventKind)nativeEvent->kind,
                            x,
                            y,
                            (MouseButton)button,
                            clickCount,
                            nativeEvent->flags
                        )
                    );
                }
                else if (nativeEvent->kind == (ushort)MouseEventKind.Move)
                {
                    // Mouse movement: 12 bytes LE (f32 x, f32 y, u32 pressed
                    // button 0-4 or 0xFFFFFFFF when none); flags hold modifiers.
                    if (
                        (nativeEvent->flags & ~0x1Fu) != 0
                        || nativeEvent->revision != 0
                        || nativeEvent->data_length != 12
                    )
                    {
                        return -112;
                    }

                    var data = new ReadOnlySpan<byte>(nativeEvent->data, 12);
                    var x = BitConverter.Int32BitsToSingle(
                        BinaryPrimitives.ReadInt32LittleEndian(data)
                    );
                    var y = BitConverter.Int32BitsToSingle(
                        BinaryPrimitives.ReadInt32LittleEndian(data[4..])
                    );
                    var pressed = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
                    if (
                        !float.IsFinite(x)
                        || !float.IsFinite(y)
                        || (pressed != 0xFFFFFFFF && pressed > (uint)MouseButton.Forward)
                    )
                    {
                        return -112;
                    }

                    session.DispatchMouseMove(
                        eventToken,
                        new MouseMoveEvent(
                            x,
                            y,
                            pressed == 0xFFFFFFFF ? null : (MouseButton)pressed,
                            nativeEvent->flags
                        )
                    );
                }
                else if (nativeEvent->kind == (ushort)ScrollEventKind.Wheel)
                {
                    // Scroll wheel: 20 bytes LE (f32 x, f32 y, f32 dx, f32 dy,
                    // u32 units 0 pixels / 1 lines); flags hold modifiers.
                    if (
                        (nativeEvent->flags & ~0x1Fu) != 0
                        || nativeEvent->revision != 0
                        || nativeEvent->data_length != 20
                    )
                    {
                        return -112;
                    }

                    var data = new ReadOnlySpan<byte>(nativeEvent->data, 20);
                    var x = BitConverter.Int32BitsToSingle(
                        BinaryPrimitives.ReadInt32LittleEndian(data)
                    );
                    var y = BitConverter.Int32BitsToSingle(
                        BinaryPrimitives.ReadInt32LittleEndian(data[4..])
                    );
                    var dx = BitConverter.Int32BitsToSingle(
                        BinaryPrimitives.ReadInt32LittleEndian(data[8..])
                    );
                    var dy = BitConverter.Int32BitsToSingle(
                        BinaryPrimitives.ReadInt32LittleEndian(data[12..])
                    );
                    var units = BinaryPrimitives.ReadUInt32LittleEndian(data[16..]);
                    if (
                        !float.IsFinite(x)
                        || !float.IsFinite(y)
                        || !float.IsFinite(dx)
                        || !float.IsFinite(dy)
                        || units > (uint)ScrollDeltaUnits.Lines
                    )
                    {
                        return -112;
                    }

                    session.DispatchScrollWheel(
                        eventToken,
                        new ScrollWheelEvent(x, y, dx, dy, (ScrollDeltaUnits)units, nativeEvent->flags)
                    );
                }
                else if (nativeEvent->kind == (ushort)FileEventKind.Dropped)
                {
                    // File drop: 8-byte LE header (f32 x, f32 y) followed by
                    // NUL-separated lossy UTF-8 paths, at least one, none empty.
                    const int MaxFileDropBytes = 1 << 20;
                    const int MaxFileDropPaths = 4096;
                    if (
                        (nativeEvent->flags & ~0x1Fu) != 0
                        || nativeEvent->revision != 0
                        || nativeEvent->data_length < 9
                        || nativeEvent->data_length > MaxFileDropBytes
                    )
                    {
                        return -112;
                    }

                    var data = new ReadOnlySpan<byte>(nativeEvent->data, nativeEvent->data_length);
                    var x = BitConverter.Int32BitsToSingle(
                        BinaryPrimitives.ReadInt32LittleEndian(data)
                    );
                    var y = BitConverter.Int32BitsToSingle(
                        BinaryPrimitives.ReadInt32LittleEndian(data[4..])
                    );
                    if (!float.IsFinite(x) || !float.IsFinite(y))
                    {
                        return -112;
                    }

                    if (!IsValidUtf8(data[8..]))
                        return -112;
                    var paths = new List<string>();
                    var segmentStart = 8;
                    for (var index = 8; index <= data.Length; index++)
                    {
                        if (index == data.Length || data[index] == 0)
                        {
                            var segment = data[segmentStart..index];
                            if (segment.IsEmpty)
                            {
                                return -112;
                            }
                            paths.Add(System.Text.Encoding.UTF8.GetString(segment));
                            segmentStart = index + 1;
                            if (paths.Count > MaxFileDropPaths)
                            {
                                return -112;
                            }
                        }
                    }
                    if (paths.Count == 0)
                    {
                        return -112;
                    }

                    session.DispatchFileDrop(
                        eventToken,
                        new FileDropEvent(x, y, paths.ToArray(), nativeEvent->flags)
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

                    if (!TryDecodeUtf8(new ReadOnlySpan<byte>(nativeEvent->data, nativeEvent->data_length), out var text))
                        return -112;
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

    private static bool IsValidUtf8(ReadOnlySpan<byte> bytes)
    {
        try
        {
            _ = NativeRegistry.StrictUtf8.GetCharCount(bytes);
            return true;
        }
        catch (System.Text.DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool TryDecodeUtf8(ReadOnlySpan<byte> bytes, out string text)
    {
        try
        {
            text = NativeRegistry.StrictUtf8.GetString(bytes);
            return true;
        }
        catch (System.Text.DecoderFallbackException)
        {
            text = string.Empty;
            return false;
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
