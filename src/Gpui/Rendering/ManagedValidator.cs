using Gpui.Interop;

namespace Gpui;

internal static unsafe class ManagedValidator
{
    internal static void Validate(RenderArena* arena, Element root)
    {
        if (root.Arena != arena || root.Generation != arena->Generation)
        {
            throw new InvalidOperationException(
                "Root does not belong to the active render generation."
            );
        }

        if (root.Node >= (uint)arena->NodeLength)
        {
            throw new InvalidOperationException("Root node is outside the node arena.");
        }

        for (var i = 0; i < arena->NodeLength; i++)
        {
            ref readonly var node = ref arena->Nodes[i];
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

        var rootComponent = (ComponentId)arena->Nodes[root.Node].Component;
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

            if (operation.B != 0 && !SemanticRegistry.AllowsPayload(code))
            {
                throw new InvalidOperationException(
                    $"Operation {i} uses payload word B outside an event binding."
                );
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

            var payloadError = SemanticRegistry.PayloadError(code, operation.A);
            if (payloadError != 0)
            {
                throw new InvalidOperationException(
                    $"Operation {i} has a payload that violates the schema constraint ({payloadError})."
                );
            }
        }

        var markedChildren = 0;
        try
        {
            for (var i = 0; i < arena->ChildLength; i++)
            {
                ref readonly var edge = ref arena->Children[i];
                if (edge.Parent >= (uint)arena->NodeLength || edge.Child >= (uint)arena->NodeLength)
                {
                    throw new InvalidOperationException(
                        $"Child edge {i} references an invalid node."
                    );
                }

                // Flags are reserved-zero on the wire. Borrow bit 0 only while validating to
                // perform an allocation-free O(E) single-parent check, then restore it below.
                ref var childNode = ref arena->Nodes[edge.Child];
                if ((childNode.Flags & 1) != 0)
                {
                    throw new InvalidOperationException(
                        $"Node {edge.Child} was attached more than once."
                    );
                }
                childNode.Flags |= 1;
                markedChildren++;

                var parentComponent = (ComponentId)arena->Nodes[edge.Parent].Component;
                var childComponent = (ComponentId)childNode.Component;
                if (
                    (parentComponent == ComponentId.Drawing) != (childComponent == ComponentId.Path)
                )
                {
                    throw new InvalidOperationException(
                        "Drawing elements may contain only Path elements, and Path elements must belong to a Drawing."
                    );
                }

                var validDockEdge = parentComponent switch
                {
                    ComponentId.DockArea => childComponent is ComponentId.DockSplit or ComponentId.DockTabs or ComponentId.DockRegion,
                    ComponentId.DockSplit => childComponent is ComponentId.DockSplit or ComponentId.DockTabs,
                    ComponentId.DockTabs => childComponent == ComponentId.DockPanel,
                    ComponentId.DockRegion => childComponent is ComponentId.DockSplit or ComponentId.DockTabs,
                    _ => childComponent is not (
                        ComponentId.DockSplit
                            or ComponentId.DockTabs
                            or ComponentId.DockPanel
                            or ComponentId.DockRegion
                    ),
                };
                if (!validDockEdge)
                {
                    throw new InvalidOperationException(
                        $"{childComponent} is not a valid child of {parentComponent}."
                    );
                }

                if (edge.Child == root.Node)
                {
                    throw new InvalidOperationException("The root node cannot have a parent.");
                }
            }

            for (var nodeIndex = 0; nodeIndex < arena->NodeLength; nodeIndex++)
            {
                var component = (ComponentId)arena->Nodes[nodeIndex].Component;
                if (component == ComponentId.Path && (arena->Nodes[nodeIndex].Flags & 1) == 0)
                {
                    throw new InvalidOperationException(
                        $"Path node {nodeIndex} must belong to a Drawing."
                    );
                }
                if (
                    component
                    is not (
                        ComponentId.Overlay
                        or ComponentId.Tooltip
                        or ComponentId.ContextMenu
                        or ComponentId.PopoverMenu
                        or ComponentId.Dynamic
                        or ComponentId.DockArea
                        or ComponentId.DockSplit
                        or ComponentId.DockTabs
                        or ComponentId.DockPanel
                        or ComponentId.DockRegion
                    )
                )
                {
                    continue;
                }

                var childCount = 0;
                for (var edgeIndex = 0; edgeIndex < arena->ChildLength; edgeIndex++)
                {
                    if (arena->Children[edgeIndex].Parent == (uint)nodeIndex)
                    {
                        childCount++;
                    }
                }
                var validCount = component switch
                {
                    ComponentId.Overlay or ComponentId.Dynamic or ComponentId.DockPanel or ComponentId.DockRegion => childCount == 1,
                    ComponentId.Tooltip or ComponentId.ContextMenu or ComponentId.PopoverMenu => childCount == 2,
                    ComponentId.DockArea => childCount is >= 1 and <= 4,
                    ComponentId.DockSplit or ComponentId.DockTabs => childCount > 0,
                    _ => false,
                };
                if (!validCount)
                {
                    throw new InvalidOperationException(
                        $"{component} node {nodeIndex} has an invalid child count."
                    );
                }
                if (component == ComponentId.DockTabs)
                {
                    var activeIndex = 0u;
                    for (var opIndex = 0; opIndex < arena->OpLength; opIndex++)
                    {
                        ref readonly var operation = ref arena->Ops[opIndex];
                        if (
                            operation.Node == (uint)nodeIndex
                            && (OpCode)operation.Code == OpCode.DockActiveIndex
                        )
                        {
                            activeIndex = checked((uint)operation.A);
                        }
                    }
                    if (activeIndex >= childCount)
                    {
                        throw new InvalidOperationException(
                            $"DockTabs node {nodeIndex} has an active index outside its panels."
                        );
                    }
                }
                if (component == ComponentId.DockArea)
                {
                    var centerCount = 0;
                    var sideMask = 0u;
                    for (var edgeIndex = 0; edgeIndex < arena->ChildLength; edgeIndex++)
                    {
                        ref readonly var edge = ref arena->Children[edgeIndex];
                        if (edge.Parent != (uint)nodeIndex)
                        {
                            continue;
                        }
                        if (
                            (ComponentId)arena->Nodes[edge.Child].Component
                            != ComponentId.DockRegion
                        )
                        {
                            centerCount++;
                            continue;
                        }

                        var side = 0u;
                        for (var opIndex = 0; opIndex < arena->OpLength; opIndex++)
                        {
                            ref readonly var operation = ref arena->Ops[opIndex];
                            if (
                                operation.Node == edge.Child
                                && (OpCode)operation.Code == OpCode.DockRegionSide
                            )
                            {
                                side = checked((uint)operation.A);
                            }
                        }
                        var sideBit = 1u << checked((int)side);
                        if ((sideMask & sideBit) != 0)
                        {
                            throw new InvalidOperationException(
                                $"DockArea node {nodeIndex} declares the same side more than once."
                            );
                        }
                        sideMask |= sideBit;
                    }
                    if (centerCount != 1)
                    {
                        throw new InvalidOperationException(
                            $"DockArea node {nodeIndex} must declare exactly one center layout."
                        );
                    }
                }
            }

            for (var left = 0; left < arena->NodeLength; left++)
            {
                if ((ComponentId)arena->Nodes[left].Component != ComponentId.DockPanel)
                {
                    continue;
                }
                var leftArea = FindDockAreaAncestor(arena, checked((uint)left));
                for (var right = left + 1; right < arena->NodeLength; right++)
                {
                    if (
                        (ComponentId)arena->Nodes[right].Component != ComponentId.DockPanel
                        || FindDockAreaAncestor(arena, checked((uint)right)) != leftArea
                    )
                    {
                        continue;
                    }
                    if (DockPanelId(arena, left).SequenceEqual(DockPanelId(arena, right)))
                    {
                        throw new InvalidOperationException(
                            $"DockArea contains duplicate panel ID '{System.Text.Encoding.UTF8.GetString(DockPanelId(arena, left))}'."
                        );
                    }
                }
            }

            // Edge validation above marks every attached child with Flags bit 0. Any
            // non-root node left unmarked was declared but never attached to the tree,
            // which native validation would only report as an opaque status code.
            for (var nodeIndex = 0; nodeIndex < arena->NodeLength; nodeIndex++)
            {
                if (
                    (uint)nodeIndex != root.Node
                    && (arena->Nodes[nodeIndex].Flags & 1) == 0
                )
                {
                    var component = (ComponentId)arena->Nodes[nodeIndex].Component;
                    throw new InvalidOperationException(
                        $"Node {nodeIndex} ({component}) was declared but never attached to the render tree."
                    );
                }
            }
        }
        finally
        {
            for (var i = 0; i < markedChildren; i++)
            {
                arena->Nodes[arena->Children[i].Child].Flags &= unchecked((ushort)~1);
            }
        }
    }

    private static uint FindDockAreaAncestor(RenderArena* arena, uint node)
    {
        while (true)
        {
            var parent = uint.MaxValue;
            for (var index = 0; index < arena->ChildLength; index++)
            {
                if (arena->Children[index].Child == node)
                {
                    parent = arena->Children[index].Parent;
                    break;
                }
            }
            if (parent == uint.MaxValue)
            {
                return parent;
            }
            if ((ComponentId)arena->Nodes[parent].Component == ComponentId.DockArea)
            {
                return parent;
            }
            node = parent;
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
