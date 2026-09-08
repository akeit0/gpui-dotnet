using System.Runtime.CompilerServices;
using Gpui.Interop;
using Gpui.Interop.Internal;

namespace Gpui;

/// <summary>
/// Stack-only render context. It cannot be captured by async/lambdas or stored on a View.
/// </summary>
public readonly unsafe ref partial struct RenderContext
{
    private readonly RenderArenaOwner _storage;
    private readonly uint _generation;
    private RenderArenaOwner _arena
    {
        get
        {
            _storage.GetArena(_generation);
            return _storage;
        }
    }

    internal RenderArenaOwner.AccessScope Access() => _storage.Access(_generation);

    internal RenderArenaOwner Storage => _arena;
    private readonly IViewRenderer? _views;
    private readonly ViewBase? _owner;
    private readonly GpuiTheme _theme;

    internal RenderContext(
        RenderArenaOwner arena,
        IViewRenderer? views = null,
        ViewBase? owner = null,
        GpuiTheme? theme = null
    )
    {
        _storage = arena;
        _generation = arena.NativeArena->Generation;
        _views = views;
        _owner = owner;
        _theme = theme ?? GpuiTheme.Default;
    }

    internal RenderArena* NativeArena => _arena.NativeArena;

    /// <summary>
    /// The active application theme. Element-only contexts use <see cref="GpuiTheme.Default"/>.
    /// </summary>
    public GpuiTheme Theme => _theme;

    internal ViewBase EventBindingOwner =>
        ViewEventRegistry.CurrentEventBindingOwner
        ?? _owner
        ?? throw new InvalidOperationException(
            "Managed title-bar menu actions require an owning View during rendering."
        );

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddInteractiveOwner(Element element)
    {
        var owner = ViewEventRegistry.CurrentEventBindingOwner ?? _owner;
        var handle = owner?.Runtime.RuntimeViewHandle ?? 0;
        if (handle != 0)
        {
            ArenaWriter.AddU32(element, OpCode.ElementOwner, handle);
        }
    }

    public Element Child<TView>(ViewSpec<TView> spec)
        where TView : View, IGeneratedViewFactory<TView> =>
        RenderManagedChild<TView>(ChildSlot.Auto);

    public Element Child<TView>(ChildKey key, ViewSpec<TView> spec)
        where TView : View, IGeneratedViewFactory<TView> =>
        RenderManagedChild<TView>(ChildSlot.Keyed(key));

    public Element Child<TView, TProps>(ViewSpec<TView, TProps> spec)
        where TProps : IEquatable<TProps>
        where TView : View<TProps>, IGeneratedViewFactory<TView, TProps> =>
        RenderManagedChild<TView, TProps>(ChildSlot.Auto, spec.Props);

    public Element Child<TView, TProps>(ChildKey key, ViewSpec<TView, TProps> spec)
        where TProps : IEquatable<TProps>
        where TView : View<TProps>, IGeneratedViewFactory<TView, TProps> =>
        RenderManagedChild<TView, TProps>(ChildSlot.Keyed(key), spec.Props);

    public void Effect<TInput>(Effect<TInput> effect, TInput input)
        where TInput : IEquatable<TInput>
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (_views is null || _owner is null)
            throw new InvalidOperationException(
                "Effects belong to retained View rendering, not virtual items."
            );
        effect.Declare(_owner, input);
    }

    private Element RenderManagedChild<TView>(ChildSlot slot)
        where TView : View, IGeneratedViewFactory<TView>
    {
        var renderer = _views ?? throw ChildRuntimeRequired();
        return renderer.RenderChild<TView>(_owner ?? throw ChildOwnerRequired(), slot, _arena);
    }

    private Element RenderManagedChild<TView, TProps>(ChildSlot slot, TProps props)
        where TProps : IEquatable<TProps>
        where TView : View<TProps>, IGeneratedViewFactory<TView, TProps>
    {
        var renderer = _views ?? throw ChildRuntimeRequired();
        return renderer.RenderChild<TView, TProps>(
            _owner ?? throw ChildOwnerRequired(),
            slot,
            in props,
            _arena
        );
    }

    private static InvalidOperationException ChildRuntimeRequired() =>
        new(
            "Managed child views require a running GPUI managed session. "
                + "RenderArenaOwner.BeginRender() is intended for element-only tests and benchmarks."
        );

    private static InvalidOperationException ChildOwnerRequired() =>
        new("A retained child view requires an owning managed View.");

    public uint Generation => _arena.NativeArena->Generation;

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
        if (arena != NativeArena)
        {
            throw new InvalidOperationException(
                "The interpolated text belongs to a different render context."
            );
        }

        return ArenaWriter.AddNode<TextTag>(_arena, ComponentId.Text, offset, length);
    }
}
