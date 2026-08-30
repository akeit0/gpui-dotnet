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

    internal Element RenderView(View view)
    {
        if (_views is null)
        {
            var ui = this;
            return view.RenderCore(ref ui);
        }

        return _views.RenderChild(
            view,
            _owner
                ?? throw new InvalidOperationException(
                    "A retained child view requires an owning managed view."
                ),
            _arena
        );
    }

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

    /// <summary>Writes already encoded UTF-8 directly into the render arena.</summary>
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
