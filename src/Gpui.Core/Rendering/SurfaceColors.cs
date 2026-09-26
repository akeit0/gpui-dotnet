using System.Runtime.CompilerServices;

namespace Gpui;

/// <summary>
/// A background and its matching inherited text color. Explicit descendant text colors and
/// native control parts retain their own colors; a surface does not recolor them.
/// </summary>
public readonly record struct SurfaceColors(Color Background, Color Foreground);

/// <summary>
/// Complete normal, hover, and pressed surface colors. The application resolves product states
/// such as selection; native GPUI selects transient interaction states.
/// </summary>
public readonly record struct InteractionColors(
    SurfaceColors Normal,
    SurfaceColors Hover,
    SurfaceColors Pressed
);

public static partial class ElementExtensions
{
    /// <summary>
    /// Declares background and inherited text color together at this point in the operation
    /// sequence. Later background or text-color declarations override their respective values.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> Surface<TTag>(this Element<TTag> element, SurfaceColors colors)
        where TTag : unmanaged, IStyledElementTag =>
        element.Background(colors.Background).TextColor(colors.Foreground);

    /// <summary>
    /// Declares complete background/text pairs for normal, hover, and pressed presentation.
    /// Writes ordinary semantic operations; native interaction requires no managed rendering.
    /// Later setters override matching properties within their own state only.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> Paint<TTag>(this Element<TTag> element, in InteractionColors colors)
        where TTag : unmanaged, IStyledElementTag, IInteractiveElementTag =>
        element
            .Surface(colors.Normal)
            .HoverBackground(colors.Hover.Background)
            .HoverTextColor(colors.Hover.Foreground)
            .ActiveBackground(colors.Pressed.Background)
            .ActiveTextColor(colors.Pressed.Foreground);
}
