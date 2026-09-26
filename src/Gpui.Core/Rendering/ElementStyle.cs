using System.Runtime.CompilerServices;

namespace Gpui;

/// <summary>
/// Open application-owned styling contract for a concrete element tag. Implementations normally
/// resolve application variants from the active <see cref="GpuiTheme"/> and apply ordinary fluent
/// element operations. Variant names and component recipes remain application concerns.
/// </summary>
/// <typeparam name="TTag">The element tag accepted by this style.</typeparam>
public interface IGpuiElementStyle<TTag>
    where TTag : unmanaged, IStyledElementTag
{
    Element<TTag> Apply(Element<TTag> element);
}

public static partial class ElementExtensions
{
    /// <summary>
    /// Applies an application-owned, strongly typed style at this point in the fluent operation
    /// sequence. Operations written afterward override matching operations written by the style.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Element<TTag> Style<TTag, TStyle>(this Element<TTag> element, in TStyle style)
        where TTag : unmanaged, IStyledElementTag
        where TStyle : struct, IGpuiElementStyle<TTag> => style.Apply(element);
}
