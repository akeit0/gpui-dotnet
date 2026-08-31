using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

public readonly unsafe ref partial struct RenderContext
{
    /// <summary>
    /// Declares window-relative content painted after normal content. Modal overlays block pointer
    /// input behind their backdrop and can dismiss through <see cref="ElementExtensions.OnDismiss"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<OverlayTag> Overlay(
        ReadOnlySpan<char> key,
        Element child,
        OverlayOptions options = default
    )
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("An overlay key cannot be empty.", nameof(key));
        }

        var element = ArenaWriter.AddNode<OverlayTag>(_arena, ComponentId.Overlay, key);
        ArenaWriter.AddU32(element.Inner, OpCode.ResourceOwner, CurrentResourceOwner());
        ConfigureOverlay(element, child, options);
        return element;
    }

    /// <summary>Declares an overlay with an already encoded UTF-8 key.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<OverlayTag> Overlay(
        ReadOnlySpan<byte> utf8Key,
        Element child,
        OverlayOptions options = default
    )
    {
        if (utf8Key.IsEmpty)
        {
            throw new ArgumentException("An overlay key cannot be empty.", nameof(utf8Key));
        }

        var element = ArenaWriter.AddNode<OverlayTag>(_arena, ComponentId.Overlay, utf8Key);
        ArenaWriter.AddU32(element.Inner, OpCode.ResourceOwner, CurrentResourceOwner());
        ConfigureOverlay(element, child, options);
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConfigureOverlay(
        Element<OverlayTag> element,
        Element child,
        OverlayOptions options
    )
    {
        ArenaWriter.AddU32(
            element.Inner,
            OpCode.OverlayPlacement,
            (uint)options.EffectivePlacement
        );
        ArenaWriter.AddU32(element.Inner, OpCode.OverlayPriority, options.EffectivePriority);
        ArenaWriter.AddF32(element.Inner, OpCode.OverlayMarginPx, options.EffectiveMargin);
        ArenaWriter.AddU32(element.Inner, OpCode.OverlayModal, options.EffectiveModal ? 1u : 0u);
        ArenaWriter.AddU32(
            element.Inner,
            OpCode.OverlayBackdropRgba,
            options.EffectiveBackdrop.Rgba
        );
        ArenaWriter.AddU32(
            element.Inner,
            OpCode.OverlayDismissOnBackdrop,
            options.EffectiveDismissOnBackdrop ? 1u : 0u
        );
        ArenaWriter.AddU32(
            element.Inner,
            OpCode.OverlayDismissOnEscape,
            options.EffectiveDismissOnEscape ? 1u : 0u
        );
        ArenaWriter.AddChild(element.Inner, child);
    }
}
