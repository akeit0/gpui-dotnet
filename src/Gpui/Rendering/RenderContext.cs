using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

/// <summary>
/// Stack-only render context. It cannot be captured by async/lambdas or stored on a View.
/// </summary>
public readonly unsafe ref partial struct RenderContext
{
    private readonly RenderArena* _arena;
    private readonly IViewRenderer? _views;
    private readonly View? _owner;

    internal RenderContext(RenderArena* arena, IViewRenderer? views = null, View? owner = null)
    {
        _arena = arena;
        _views = views;
        _owner = owner;
    }

    internal RenderArena* NativeArena => _arena;

    /// <summary>
    /// Renders a framework-owned child in the next positional child slot. The instance is created
    /// once per retained slot and reused while the slot continues to request the same view type.
    /// </summary>
    public Element Child<TView>()
        where TView : View, IGeneratedViewFactory<TView> =>
        RenderManagedChild<TView>(ChildSlot.Auto);

    /// <summary>
    /// Renders a framework-owned child in a stable keyed slot. If the same key later requests a
    /// different view type, the old view is unmounted after the new tree commits and the slot is
    /// replaced.
    /// </summary>
    public Element Child<TView>(ChildKey key)
        where TView : View, IGeneratedViewFactory<TView> =>
        RenderManagedChild<TView>(ChildSlot.Keyed(key));

    /// <summary>Renders a framework-owned child with parent-supplied props.</summary>
    public Element Child<TView, TProps>(in TProps props)
        where TView : View<TProps>, IGeneratedViewFactory<TView> =>
        RenderManagedChild<TView, TProps>(ChildSlot.Auto, in props);

    /// <summary>Renders a keyed framework-owned child with parent-supplied props.</summary>
    public Element Child<TView, TProps>(ChildKey key, in TProps props)
        where TView : View<TProps>, IGeneratedViewFactory<TView> =>
        RenderManagedChild<TView, TProps>(ChildSlot.Keyed(key), in props);

    /// <summary>
    /// Renders an explicitly owned View instance in the next positional slot. Use this escape hatch
    /// when the parent intentionally needs an imperative reference to the child.
    /// </summary>
    public Element Child(View view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return RenderExplicitChild(view, ChildSlot.Auto);
    }

    /// <summary>Renders an explicitly owned View instance in a stable keyed slot.</summary>
    public Element Child(View view, ChildKey key)
    {
        ArgumentNullException.ThrowIfNull(view);
        return RenderExplicitChild(view, ChildSlot.Keyed(key));
    }

    /// <summary>Renders an explicitly owned props View in the next positional slot.</summary>
    public Element Child<TProps>(View<TProps> view, in TProps props)
    {
        ArgumentNullException.ThrowIfNull(view);
        return RenderExplicitChild(view, ChildSlot.Auto, in props);
    }

    /// <summary>Renders an explicitly owned props View in a stable keyed slot.</summary>
    public Element Child<TProps>(View<TProps> view, ChildKey key, in TProps props)
    {
        ArgumentNullException.ThrowIfNull(view);
        return RenderExplicitChild(view, ChildSlot.Keyed(key), in props);
    }

    private Element RenderManagedChild<TView>(ChildSlot slot)
        where TView : View, IGeneratedViewFactory<TView>
    {
        var renderer = _views ?? throw ChildRuntimeRequired();
        var owner = _owner ?? throw ChildOwnerRequired();
        return renderer.RenderChild<TView>(owner, slot, _arena);
    }

    private Element RenderManagedChild<TView, TProps>(ChildSlot slot, in TProps props)
        where TView : View<TProps>, IGeneratedViewFactory<TView>
    {
        var renderer = _views ?? throw ChildRuntimeRequired();
        var owner = _owner ?? throw ChildOwnerRequired();
        return renderer.RenderChild<TView, TProps>(owner, slot, in props, _arena);
    }

    private Element RenderExplicitChild(View view, ChildSlot slot)
    {
        var renderer = _views ?? throw ChildRuntimeRequired();
        var owner = _owner ?? throw ChildOwnerRequired();
        return renderer.RenderChild(view, owner, slot, _arena);
    }

    private Element RenderExplicitChild<TProps>(View<TProps> view, ChildSlot slot, in TProps props)
    {
        var renderer = _views ?? throw ChildRuntimeRequired();
        var owner = _owner ?? throw ChildOwnerRequired();
        return renderer.RenderChild(view, owner, slot, in props, _arena);
    }

    private static InvalidOperationException ChildRuntimeRequired() =>
        new(
            "Managed child views require a running GPUI managed session. "
                + "RenderArenaOwner.BeginRender() is intended for element-only tests and benchmarks."
        );

    private static InvalidOperationException ChildOwnerRequired() =>
        new("A retained child view requires an owning managed View.");

    public uint Generation => _arena->Generation;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<DivTag> VStack(params ReadOnlySpan<Element> children)
    {
        var element = Div(children);
        ArenaWriter.AddNoArg(element.Inner, OpCode.VStack);
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<DivTag> HStack(params ReadOnlySpan<Element> children)
    {
        var element = Div(children);
        ArenaWriter.AddNoArg(element.Inner, OpCode.Flex);
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<TextTag> Text(ReadOnlySpan<char> text) =>
        ArenaWriter.AddNode<TextTag>(_arena, ComponentId.Text, text);

    /// <summary>
    /// Writes already encoded UTF-8 directly into the render arena. The bytes are trusted:
    /// they must contain valid UTF-8 with no interior NUL, and must remain unmodified for the
    /// duration of the call. Invalid bytes are rejected by native validation, failing the frame.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<TextTag> Text(ReadOnlySpan<byte> utf8) =>
        ArenaWriter.AddNode<TextTag>(_arena, ComponentId.Text, utf8);

    /// <summary>Formats an interpolated string directly into the UTF-8 render arena.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<TextTag> Text(
        [InterpolatedStringHandlerArgument("")] ref Utf8InterpolatedStringHandler text
    )
    {
        text.Complete(out var arena, out var offset, out var length);
        if (arena != _arena)
        {
            throw new InvalidOperationException(
                "The interpolated text belongs to a different render context."
            );
        }

        return ArenaWriter.AddNode<TextTag>(_arena, ComponentId.Text, offset, length);
    }
}
