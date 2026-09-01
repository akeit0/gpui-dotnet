using Gpui;

namespace Gpui.Interop.Internal.Session;

internal readonly struct ChildEntry
{
    internal ChildEntry(ViewBase view) => View = view;

    internal ViewBase View { get; }
}
