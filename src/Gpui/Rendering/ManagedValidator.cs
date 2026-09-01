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
                var expectedCount = component is ComponentId.Overlay or ComponentId.Dynamic ? 1 : 2;
                if (childCount != expectedCount)
                {
                    throw new InvalidOperationException(
                        $"{component} node {nodeIndex} must have exactly {expectedCount} children."
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
}
