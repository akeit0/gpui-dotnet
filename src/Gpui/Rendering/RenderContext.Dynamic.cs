using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

public readonly unsafe ref partial struct RenderContext
{
    /// <summary>
    /// Transparently wraps one element and requests display-synchronized managed renders while
    /// <paramref name="active"/> is true. Use this for app-defined interpolation, timelines, and
    /// physics whose target tree is rebuilt in C# on each display frame.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<DynamicTag> Dynamic(bool active, Element child)
    {
        var element = ArenaWriter.AddNode<DynamicTag>(_arena, ComponentId.Dynamic);
        ArenaWriter.AddU32(element.Inner, OpCode.ResourceOwner, CurrentResourceOwner());
        ArenaWriter.AddU32(element.Inner, OpCode.DynamicActive, active ? 1u : 0u);
        ArenaWriter.AddChild(element.Inner, child);
        return element;
    }

    /// <summary>Requests managed renders on every display frame while this wrapper is present.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<DynamicTag> Dynamic(Element child) => Dynamic(true, child);
}
