using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

/// <summary>Native platform hit-test behavior for a custom title-bar region.</summary>
public enum WindowControlArea : uint
{
    Drag,
    Minimize,
    Maximize,
    Close,
}

public static partial class ElementExtensions
{
    /// <summary>
    /// Marks this element's bounds as a native title-bar drag or caption-button region.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> WindowControlArea<TTag>(
        this Element<TTag> element,
        WindowControlArea area
    )
        where TTag : unmanaged, IWindowControlElementTag
    {
        if (!Enum.IsDefined(area))
        {
            throw new ArgumentOutOfRangeException(nameof(area));
        }

        ArenaWriter.AddU32(element.Inner, OpCode.WindowControlArea, (uint)area);
        return element;
    }
}
