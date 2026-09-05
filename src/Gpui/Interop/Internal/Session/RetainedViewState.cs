using Gpui;

namespace Gpui.Interop.Internal.Session;

/// <summary>
/// Application-thread-owned composition and fragment state for one View.
/// </summary>
internal sealed class RetainedViewState
{
    internal ViewBase? Parent;
    internal RenderArenaOwner? Fragment;
    internal uint Root;
    internal bool Dirty = true;
    internal ReactiveConsumer? Consumer;
    internal uint WorkingNextPosition;
    internal bool HasStagedComposition;
    internal Dictionary<ChildSlot, ChildEntry>? Children;
    internal Dictionary<ChildSlot, ChildEntry>? WorkingChildren;
    internal Dictionary<ChildSlot, ChildEntry>? StagedChildren;
    internal Dictionary<ChildSlot, ChildEntry>? Candidates;
    internal HashSet<ViewBase>? WorkingViews;
}
