using System.Text;

namespace Gpui.Components;

/// <summary>A node in a preorder tree batch. Depth zero starts a root.</summary>
public readonly record struct ComponentTreeNode(
    string Id,
    string Label,
    uint Depth,
    bool Disabled = false,
    bool InitiallyExpanded = false
);

public sealed record ComponentTreeOptions
{
    /// <summary>Application-owned committed selection, separate from the native keyboard cursor.</summary>
    public string? SelectedId { get; init; }
}

public enum ComponentTreeEventKind
{
    SelectionRequested,
    Expanded,
    Collapsed,
}

/// <summary>A copied stable node ID from native tree interaction.</summary>
public sealed class ComponentTreeEvent : INativeExtensionEvent<ComponentTreeEvent>
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private ComponentTreeEvent(ComponentTreeEventKind kind, string itemId)
    {
        Kind = kind;
        ItemId = itemId;
    }

    public ComponentTreeEventKind Kind { get; }
    public string ItemId { get; }

    public static ComponentTreeEvent Decode(NativeExtensionEvent nativeEvent)
    {
        ArgumentNullException.ThrowIfNull(nativeEvent);
        var kind = nativeEvent.Kind switch
        {
            ComponentSchema.Tree.EventSelectionRequested =>
                ComponentTreeEventKind.SelectionRequested,
            ComponentSchema.Tree.EventExpanded => ComponentTreeEventKind.Expanded,
            ComponentSchema.Tree.EventCollapsed => ComponentTreeEventKind.Collapsed,
            _ => throw new InvalidOperationException("The Tree event kind is invalid."),
        };
        if (
            nativeEvent.Flags != 0
            || nativeEvent.Revision != 0
            || nativeEvent.Payload.IsEmpty
            || nativeEvent.Payload.Length > 256
        )
            throw new InvalidOperationException("The Tree event metadata is invalid.");
        string id;
        try
        {
            id = StrictUtf8.GetString(nativeEvent.Payload.Span);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidOperationException("The Tree item ID is not UTF-8.", exception);
        }
        if (string.IsNullOrWhiteSpace(id) || id.Any(char.IsControl))
            throw new InvalidOperationException("The Tree item ID is invalid.");
        return new(kind, id);
    }
}

public static partial class ComponentElements
{
    /// <summary>Declares a virtualized tree from one preorder node batch.</summary>
    public static Element<NativeExtensionTag> Tree(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        ComponentTreeOptions? options = null,
        params ReadOnlySpan<ComponentTreeNode> nodes
    ) => Tree(ui, key, 0, options, nodes);

    public static Element<NativeExtensionTag> Tree<TView>(
        this RenderContext ui,
        ReadOnlySpan<char> key,
        TView view,
        Action<TView, ComponentTreeEvent> onEvent,
        ComponentTreeOptions? options = null,
        params ReadOnlySpan<ComponentTreeNode> nodes
    )
        where TView : ViewBase =>
        Tree(ui, key, ui.BindNativeExtensionEvent(view, onEvent).Token, options, nodes);

    private static Element<NativeExtensionTag> Tree(
        RenderContext ui,
        ReadOnlySpan<char> key,
        ulong token,
        ComponentTreeOptions? options,
        ReadOnlySpan<ComponentTreeNode> nodes
    )
    {
        options ??= new();
        var batch = TreeBatch.Create(nodes, options.SelectedId);
        return ui.NativeExtension(
            ComponentsExtension.Tree,
            key,
            ComponentSchema.Tree.EncodeConfiguration(
                batch.Ids,
                batch.Labels,
                batch.Depths,
                batch.Disabled,
                batch.InitiallyExpanded,
                options.SelectedId ?? string.Empty,
                options.SelectedId is not null,
                token
            )
        );
    }
}

internal sealed class TreeBatch
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private TreeBatch(
        string[] ids,
        string[] labels,
        uint[] depths,
        uint[] disabled,
        uint[] initiallyExpanded
    )
    {
        Ids = ids;
        Labels = labels;
        Depths = depths;
        Disabled = disabled;
        InitiallyExpanded = initiallyExpanded;
    }

    public string[] Ids { get; }
    public string[] Labels { get; }
    public uint[] Depths { get; }
    public uint[] Disabled { get; }
    public uint[] InitiallyExpanded { get; }

    public static TreeBatch Create(ReadOnlySpan<ComponentTreeNode> nodes, string? selectedId)
    {
        if (nodes.Length > 4096)
            throw new ArgumentException("Tree batches support at most 4096 nodes.", nameof(nodes));
        var ids = new string[nodes.Length];
        var labels = new string[nodes.Length];
        var depths = new uint[nodes.Length];
        var disabled = new uint[nodes.Length];
        var expanded = new uint[nodes.Length];
        var known = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < nodes.Length; index++)
        {
            var node = nodes[index];
            if (string.IsNullOrWhiteSpace(node.Id) || string.IsNullOrWhiteSpace(node.Label))
                throw new ArgumentException("Tree nodes need IDs and labels.", nameof(nodes));
            int idBytes;
            try
            {
                idBytes = StrictUtf8.GetByteCount(node.Id);
                _ = StrictUtf8.GetByteCount(node.Label);
            }
            catch (EncoderFallbackException exception)
            {
                throw new ArgumentException(
                    "Tree text must be valid Unicode.",
                    nameof(nodes),
                    exception
                );
            }
            if (
                node.Id.Any(char.IsControl)
                || idBytes > 256
                || !known.Add(node.Id)
                || node.Label.Any(char.IsControl)
                || node.Depth > 64
                || (index == 0 && node.Depth != 0)
                || (index > 0 && node.Depth > depths[index - 1] + 1)
            )
                throw new ArgumentException(
                    "Tree nodes need unique IDs, labels, and preorder depths.",
                    nameof(nodes)
                );
            ids[index] = node.Id;
            labels[index] = node.Label;
            depths[index] = node.Depth;
            disabled[index] = node.Disabled ? 1u : 0u;
            expanded[index] = node.InitiallyExpanded ? 1u : 0u;
        }
        if (selectedId is not null && !known.Contains(selectedId))
            throw new ArgumentException(
                "The selected Tree ID is absent from the nodes.",
                nameof(selectedId)
            );
        return new(ids, labels, depths, disabled, expanded);
    }
}
