using Gpui;

namespace Gpui.Interop.Internal.Session;

internal enum ChildOwnership : byte
{
    Framework,
    Explicit,
}

internal readonly struct ChildEntry
{
    internal ChildEntry(View view, ChildOwnership ownership)
    {
        View = view;
        Ownership = ownership;
    }

    internal View View { get; }
    internal ChildOwnership Ownership { get; }
}
