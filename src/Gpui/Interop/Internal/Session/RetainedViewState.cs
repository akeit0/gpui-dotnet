using Gpui;

namespace Gpui.Interop.Internal.Session;

/// <summary>
/// Retained per-View render state. Extracted from ManagedSession to give each class its own file (single responsibility).
/// </summary>
internal sealed class RetainedViewState
{
    internal ViewBase? Parent;
    internal RenderArenaOwner? Fragment;
    internal uint Root;
    internal long RequiredVersion = 1;
    internal long RenderedVersion;
    internal uint WorkingNextPosition;
    internal bool HasStagedComposition;
    internal Dictionary<ChildSlot, ChildEntry>? Children;
    internal Dictionary<ChildSlot, ChildEntry>? WorkingChildren;
    internal Dictionary<ChildSlot, ChildEntry>? StagedChildren;
    internal Dictionary<ChildSlot, ChildEntry>? Candidates;
    internal HashSet<ViewBase>? WorkingViews;
}
