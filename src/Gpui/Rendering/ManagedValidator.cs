using System.Buffers;
using Gpui.Interop;

namespace Gpui;

internal static unsafe class ManagedValidator
{
    internal static void ValidateRoot(RenderArena* arena, Element root)
    {
        if (root.Owner is null || root.Owner.NativeArena != arena || root.Generation != arena->Generation)
        {
            throw new InvalidOperationException(
                "Root does not belong to the active render generation."
            );
        }

        if (root.Node >= (uint)arena->NodeLength)
        {
            throw new InvalidOperationException("Root node is outside the node arena.");
        }
    }

    internal static void Validate(RenderArena* arena, Element root)
    {
        ValidateRoot(arena, root);
        using var access = root.Access();
        Validate(arena, root.Node);
    }

    // Trusted borrowed descriptors are validated without manufacturing an authoring Element.
    internal static void Validate(RenderArena* arena, uint root)
    {
        if (root >= (uint)arena->NodeLength)
            throw new InvalidOperationException("Root node is outside the node arena.");
        NodeInfo[]? rented = null;
        Span<NodeInfo> nodes = arena->NodeLength <= 128
            ? stackalloc NodeInfo[arena->NodeLength]
            : (rented = ArrayPool<NodeInfo>.Shared.Rent(arena->NodeLength)).AsSpan(0, arena->NodeLength);
        try { ValidateCore(arena, root, nodes); }
        finally { if (rented is not null) ArrayPool<NodeInfo>.Shared.Return(rented); }
    }

    private static void ValidateCore(RenderArena* arena, uint root, Span<NodeInfo> nodes)
    {
        var panelCount = 0;
        for (var i = 0; i < arena->NodeLength; i++)
        {
            ref readonly var node = ref arena->Nodes[i];
            nodes[i] = new NodeInfo
            {
                Parent = -1,
                DockArea = UnknownArea,
            };
            if ((ComponentId)node.Component == ComponentId.DockPanel) panelCount++;
            if (node.Flags != 0)
            {
                throw new InvalidOperationException(
                    $"Node {i} uses reserved flags 0x{node.Flags:X4}."
                );
            }

            if (!SemanticRegistry.IsKnownComponent((ComponentId)node.Component))
            {
                throw new InvalidOperationException(
                    $"Node {i} has unknown component {node.Component}."
                );
            }

            if ((ulong)node.DataOffset + node.DataLength > (ulong)arena->Utf8Length)
            {
                throw new InvalidOperationException($"Node {i} has invalid UTF-8 range.");
            }

            var componentId = (ComponentId)node.Component;
            if (SemanticRegistry.IsDataRequired(componentId) && node.DataLength == 0)
            {
                throw new InvalidOperationException(
                    $"{componentId} node {i} requires non-empty data."
                );
            }

            if ((ComponentId)node.Component == ComponentId.Input)
            {
                var payload = new ReadOnlySpan<byte>(
                    arena->Utf8 + node.DataOffset,
                    checked((int)node.DataLength)
                );
                var firstSeparator = payload.IndexOf((byte)0);
                var secondSeparator =
                    firstSeparator < 0 ? -1 : payload[(firstSeparator + 1)..].IndexOf((byte)0);
                if (
                    firstSeparator <= 0
                    || secondSeparator < 0
                    || payload[(firstSeparator + secondSeparator + 2)..].Contains((byte)0)
                )
                {
                    throw new InvalidOperationException(
                        $"Input node {i} must contain a non-empty key, initial value, and placeholder."
                    );
                }
            }
            else if ((ComponentId)node.Component == ComponentId.Slider)
            {
                var payload = new ReadOnlySpan<byte>(
                    arena->Utf8 + node.DataOffset,
                    checked((int)node.DataLength)
                );
                if (payload.Contains((byte)0))
                {
                    throw new InvalidOperationException(
                        $"Slider node {i} must contain a single resource key."
                    );
                }
            }
            else if ((ComponentId)node.Component == ComponentId.DockArea)
            {
                var payload = new ReadOnlySpan<byte>(
                    arena->Utf8 + node.DataOffset,
                    checked((int)node.DataLength)
                );
                if (payload.Contains((byte)0))
                {
                    throw new InvalidOperationException(
                        $"DockArea node {i} must contain a single resource key."
                    );
                }
            }
            else if ((ComponentId)node.Component == ComponentId.DockPanel)
            {
                var payload = new ReadOnlySpan<byte>(
                    arena->Utf8 + node.DataOffset,
                    checked((int)node.DataLength)
                );
                var firstSeparator = payload.IndexOf((byte)0);
                var remainder = firstSeparator < 0 ? default : payload[(firstSeparator + 1)..];
                var secondSeparator = remainder.IndexOf((byte)0);
                if (
                    firstSeparator <= 0
                    || secondSeparator < 0
                    || !remainder[(secondSeparator + 1)..].IsEmpty
                )
                {
                    throw new InvalidOperationException(
                        $"DockPanel node {i} must contain a non-empty ID and a terminated title."
                    );
                }
            }
            else if ((ComponentId)node.Component == ComponentId.NativeExtension)
            {
                var payload = new ReadOnlySpan<byte>(
                    arena->Utf8 + node.DataOffset,
                    checked((int)node.DataLength)
                );
                if (!IsValidNativeExtensionPayload(payload))
                {
                    throw new InvalidOperationException(
                        $"NativeExtension node {i} has a malformed extension envelope."
                    );
                }
            }
        }

        var rootComponent = (ComponentId)arena->Nodes[root].Component;
        if (
            rootComponent
            is ComponentId.DockSplit
                or ComponentId.DockTabs
                or ComponentId.DockPanel
                or ComponentId.DockRegion
        )
        {
            throw new InvalidOperationException(
                $"{rootComponent} must be contained by a DockArea declaration."
            );
        }

        for (var i = 0; i < arena->OpLength; i++)
        {
            ref readonly var operation = ref arena->Ops[i];
            if (operation.Node >= (uint)arena->NodeLength)
            {
                throw new InvalidOperationException($"Operation {i} references an invalid node.");
            }

            var code = (OpCode)operation.Code;
            var expectedKind = SemanticRegistry.ExpectedValueKind(code);
            if (expectedKind is null)
            {
                throw new InvalidOperationException(
                    $"Operation {i} has unknown code {operation.Code}."
                );
            }

            if (operation.ValueKind != (ushort)expectedKind.Value)
            {
                throw new InvalidOperationException(
                    $"Operation {i} has value kind {operation.ValueKind}; expected {(ushort)expectedKind.Value}."
                );
            }

            if (
                operation.B != 0
                && expectedKind != ValueKind.Data
                && !SemanticRegistry.AllowsPayload(code)
            )
            {
                throw new InvalidOperationException(
                    $"Operation {i} uses payload word B outside an event binding."
                );
            }

            if (expectedKind == ValueKind.Data)
            {
                var dataEnd = operation.A + operation.B;
                if (
                    operation.B == 0
                    || dataEnd < operation.A
                    || dataEnd > (ulong)arena->Utf8Length
                )
                {
                    throw new InvalidOperationException(
                        $"Operation {i} has a data range outside the UTF-8 arena."
                    );
                }
            }

            if (expectedKind == ValueKind.None && operation.A != 0)
            {
                throw new InvalidOperationException(
                    $"Operation {i} has a payload for a no-value operation."
                );
            }

            if (expectedKind is ValueKind.F32 or ValueKind.U32 && (operation.A >> 32) != 0)
            {
                throw new InvalidOperationException(
                    $"Operation {i} has non-canonical scalar payload bits."
                );
            }

            var component = (ComponentId)arena->Nodes[operation.Node].Component;
            if (!SemanticRegistry.IsAllowed(component, code))
            {
                throw new InvalidOperationException(
                    $"Operation {code} is not valid for component {component}."
                );
            }

            if (
                expectedKind == ValueKind.F32
                && !float.IsFinite(BitConverter.UInt32BitsToSingle((uint)operation.A))
            )
            {
                throw new InvalidOperationException(
                    $"Operation {i} has a non-finite numeric value."
                );
            }

            if (
                expectedKind == ValueKind.F32x2
                && (
                    !float.IsFinite(BitConverter.UInt32BitsToSingle((uint)operation.A))
                    || !float.IsFinite(BitConverter.UInt32BitsToSingle((uint)(operation.A >> 32)))
                )
            )
            {
                throw new InvalidOperationException(
                    $"Operation {i} has a non-finite coordinate value."
                );
            }

            if (expectedKind == ValueKind.Callback && operation.A == 0)
            {
                throw new InvalidOperationException($"Operation {i} uses reserved event token 0.");
            }

            var payloadError = SemanticRegistry.PayloadError(code, operation.A, operation.B);
            if (payloadError != 0)
            {
                throw new InvalidOperationException(
                    $"Operation {i} has a payload that violates the schema constraint ({payloadError})."
                );
            }
            if (code is OpCode.DockActiveIndex or OpCode.DockRegionSide)
                nodes[(int)operation.Node].ComponentValue = (uint)operation.A;
            else if (code == OpCode.TableColumn)
                nodes[(int)operation.Node].ComponentValue++;
        }

        for (var i = 0; i < arena->ChildLength; i++)
        {
            ref readonly var edge = ref arena->Children[i];
            if (edge.Parent >= (uint)nodes.Length || edge.Child >= (uint)nodes.Length)
                throw new InvalidOperationException($"Child edge {i} references an invalid node.");

            ref var child = ref nodes[(int)edge.Child];
            if (child.Parent != -1)
                throw new InvalidOperationException($"Node {edge.Child} was attached more than once.");
            if (edge.Child == root)
                throw new InvalidOperationException("The root node cannot have a parent.");
            child.Parent = (int)edge.Parent;
            ref var parent = ref nodes[(int)edge.Parent];
            parent.ChildCount++;

            var parentComponent = (ComponentId)arena->Nodes[edge.Parent].Component;
            var childComponent = (ComponentId)arena->Nodes[edge.Child].Component;
            if ((parentComponent == ComponentId.Drawing) != (childComponent == ComponentId.Path))
                throw new InvalidOperationException(
                    "Drawing elements may contain only Path elements, and Path elements must belong to a Drawing.");

            var validDockEdge = parentComponent switch
            {
                ComponentId.DockArea => childComponent is ComponentId.DockSplit or ComponentId.DockTabs or ComponentId.DockRegion,
                ComponentId.DockSplit => childComponent is ComponentId.DockSplit or ComponentId.DockTabs,
                ComponentId.DockTabs => childComponent == ComponentId.DockPanel,
                ComponentId.DockRegion => childComponent is ComponentId.DockSplit or ComponentId.DockTabs,
                _ => childComponent is not (ComponentId.DockSplit or ComponentId.DockTabs or ComponentId.DockPanel or ComponentId.DockRegion),
            };
            if (!validDockEdge)
                throw new InvalidOperationException($"{childComponent} is not a valid child of {parentComponent}.");

            if (parentComponent == ComponentId.DockArea)
            {
                if (childComponent == ComponentId.DockRegion)
                {
                    var side = 1u << (int)child.ComponentValue;
                    if ((parent.SideMask & side) != 0)
                        throw new InvalidOperationException($"DockArea node {edge.Parent} declares the same side more than once.");
                    parent.SideMask |= side;
                }
                else parent.CenterCount++;
            }
        }

        for (var index = 0; index < nodes.Length; index++)
        {
            ref var node = ref nodes[index];
            var component = (ComponentId)arena->Nodes[index].Component;
            if (component == ComponentId.Path && node.Parent == -1)
                throw new InvalidOperationException($"Path node {index} must belong to a Drawing.");
            if ((uint)index != root && node.Parent == -1)
                throw new InvalidOperationException($"Node {index} ({component}) was declared but never attached to the render tree.");

            var validCount = component switch
            {
                ComponentId.Overlay or ComponentId.Dynamic or ComponentId.DockPanel or ComponentId.DockRegion => node.ChildCount == 1,
                ComponentId.Tooltip or ComponentId.ContextMenu or ComponentId.PopoverMenu => node.ChildCount == 2,
                ComponentId.DockArea => node.ChildCount is >= 1 and <= 4,
                ComponentId.DockSplit or ComponentId.DockTabs => node.ChildCount > 0,
                ComponentId.Table => node.ChildCount == 0 || node.ChildCount == node.ComponentValue,
                _ => true,
            };
            if (!validCount)
                throw new InvalidOperationException($"{component} node {index} has an invalid child count.");
            if (component == ComponentId.DockTabs && node.ComponentValue >= node.ChildCount)
                throw new InvalidOperationException($"DockTabs node {index} has an active index outside its panels.");
            if (component == ComponentId.DockArea && node.CenterCount != 1)
                throw new InvalidOperationException($"DockArea node {index} must declare exactly one center layout.");
        }

        // Resolve each parent path once, independent of declaration/edge order. The scratch
        // chain unwinds ancestor-first to compute nearest Dock areas and detect disconnected cycles.
        for (var index = 0; index < nodes.Length; index++)
        {
            if (nodes[index].DockArea != UnknownArea) continue;
            var current = index;
            var path = -1;
            while (current != -1 && nodes[current].DockArea == UnknownArea)
            {
                nodes[current].DockArea = VisitingArea;
                nodes[current].PathPrevious = path;
                path = current;
                current = nodes[current].Parent;
            }
            if (current != -1 && nodes[current].DockArea == VisitingArea)
                throw new InvalidOperationException("The render tree contains a cycle.");
            var area = current == -1 ? -1 : nodes[current].DockArea;
            while (path != -1)
            {
                if ((ComponentId)arena->Nodes[path].Component == ComponentId.DockArea) area = path;
                nodes[path].DockArea = area;
                path = nodes[path].PathPrevious;
            }
        }
        ValidatePanelIds(arena, nodes, panelCount);
    }

    private const int UnknownArea = -2;
    private const int VisitingArea = -3;

    private struct NodeInfo
    {
        internal int Parent;
        internal int ChildCount;
        // Dock active index/region side, or Table column count; component kinds are exclusive.
        internal uint ComponentValue;
        internal int CenterCount;
        internal uint SideMask;
        internal int DockArea;
        internal int PathPrevious;
    }

    private readonly unsafe struct PanelId(int area, byte* bytes, int length) : IComparable<PanelId>
    {
        internal readonly int Area = area;
        internal readonly byte* Bytes = bytes;
        internal readonly int Length = length;

        public int CompareTo(PanelId other)
        {
            var areaOrder = Area.CompareTo(other.Area);
            return areaOrder != 0 ? areaOrder
                : new ReadOnlySpan<byte>(Bytes, Length).SequenceCompareTo(new ReadOnlySpan<byte>(other.Bytes, other.Length));
        }
    }

    private static void ValidatePanelIds(RenderArena* arena, ReadOnlySpan<NodeInfo> nodes, int count)
    {
        if (count < 2) return;
        PanelId[]? rented = null;
        Span<PanelId> panels = count <= 128 ? stackalloc PanelId[count]
            : (rented = ArrayPool<PanelId>.Shared.Rent(count)).AsSpan(0, count);
        try
        {
            var next = 0;
            for (var index = 0; index < nodes.Length; index++)
            {
                if ((ComponentId)arena->Nodes[index].Component != ComponentId.DockPanel) continue;
                var id = DockPanelId(arena, index);
                panels[next++] = new PanelId(nodes[index].DockArea, arena->Utf8 + arena->Nodes[index].DataOffset, id.Length);
            }
            // Sort UTF-8 slices, without allocating strings or hashing application-controlled IDs.
            panels.Sort();
            for (var index = 1; index < panels.Length; index++)
                if (panels[index - 1].CompareTo(panels[index]) == 0)
                    throw new InvalidOperationException(
                        $"DockArea contains duplicate panel ID '{System.Text.Encoding.UTF8.GetString(new ReadOnlySpan<byte>(panels[index].Bytes, panels[index].Length))}'.");
        }
        finally
        {
            // Scratch never retains pointers into a completed arena borrow.
            panels.Clear();
            if (rented is not null) ArrayPool<PanelId>.Shared.Return(rented);
        }
    }

    private static ReadOnlySpan<byte> DockPanelId(RenderArena* arena, int nodeIndex)
    {
        ref readonly var node = ref arena->Nodes[nodeIndex];
        var payload = new ReadOnlySpan<byte>(
            arena->Utf8 + node.DataOffset,
            checked((int)node.DataLength)
        );
        return payload[..payload.IndexOf((byte)0)];
    }

    private static bool IsValidNativeExtensionPayload(ReadOnlySpan<byte> payload)
    {
        var remaining = payload;
        if (
            !TryTakeExtensionField(ref remaining, out var extensionId)
            || !TryTakeExtensionField(ref remaining, out var componentKind)
            || !TryTakeExtensionField(ref remaining, out var key)
            || !TryTakeExtensionField(ref remaining, out var version)
            || !TryTakeExtensionField(ref remaining, out var schemaHash)
            || remaining.Contains((byte)0)
        )
        {
            return false;
        }

        return IsExtensionIdentifier(extensionId)
            && IsExtensionIdentifier(componentKind)
            && !key.IsEmpty
            && key[0] >= 0x20
            && key.IndexOfAnyInRange((byte)0, (byte)0x1F) < 0
            && TryParseNonZeroDecimal(version)
            && IsNonZeroSchemaHash(schemaHash);
    }

    private static bool TryTakeExtensionField(
        ref ReadOnlySpan<byte> remaining,
        out ReadOnlySpan<byte> field
    )
    {
        var separator = remaining.IndexOf((byte)0);
        if (separator < 0)
        {
            field = default;
            return false;
        }
        field = remaining[..separator];
        remaining = remaining[(separator + 1)..];
        return true;
    }

    private static bool IsExtensionIdentifier(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty || value.Length > 127)
        {
            return false;
        }
        foreach (var character in value)
        {
            if (
                character is not (
                    >= (byte)'a' and <= (byte)'z'
                    or >= (byte)'A' and <= (byte)'Z'
                    or >= (byte)'0' and <= (byte)'9'
                    or (byte)'.'
                    or (byte)'-'
                    or (byte)'_'
                )
            )
            {
                return false;
            }
        }
        return true;
    }

    private static bool TryParseNonZeroDecimal(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty || value.Length > 10)
        {
            return false;
        }
        uint result = 0;
        foreach (var digit in value)
        {
            if (digit is < (byte)'0' or > (byte)'9')
            {
                return false;
            }
            try
            {
                result = checked(result * 10 + (uint)(digit - (byte)'0'));
            }
            catch (OverflowException)
            {
                return false;
            }
        }
        return result != 0;
    }

    private static bool IsAsciiHex(byte value) =>
        value is >= (byte)'0' and <= (byte)'9'
            or >= (byte)'A' and <= (byte)'F';

    private static bool IsNonZeroSchemaHash(ReadOnlySpan<byte> value)
    {
        if (value.Length != 16)
        {
            return false;
        }
        var nonZero = false;
        foreach (var digit in value)
        {
            if (!IsAsciiHex(digit))
            {
                return false;
            }
            nonZero |= digit != (byte)'0';
        }
        return nonZero;
    }
}
