using static Gpui.Colors;
using static Gpui.Units;

namespace Gpui.Net.Tests;

public sealed class SemanticRenderTests
{
    [Fact]
    public void RepresentativeTreePassesManagedValidation()
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        var root = ui.VStack(
                ui.Text("hello"u8),
                ui.Button("save", "Save"u8).Padding(Px(8)).Background(Rgb(37, 99, 235)),
                ui.Image("asset.svg"u8).Fit(ImageFit.Cover).Grayscale()
            )
            .Gap(Px(6));

        arena.Validate(root);

        var stats = arena.GetStats();
        Assert.Equal(5, stats.Nodes);
        Assert.Equal(4, stats.Children);
        Assert.True(stats.Ops >= 5);
    }

    [Fact]
    public void ElementCannotEscapeItsRenderGeneration()
    {
        using var arena = new RenderArenaOwner();
        var first = arena.BeginRender();
        var stale = first.Div();
        var second = arena.BeginRender();
        var root = second.Div();

        Assert.Throws<InvalidOperationException>(() => root.Child(stale));
    }

    [Fact]
    public void InvalidImageFitIsRejectedAtTheManagedBoundary()
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        var image = ui.Image("asset.svg"u8);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            image.Fit((ImageFit)99)
        );
    }

    [Fact]
    public void Utf8InputConfigurationPassesManagedValidation()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            using var arena = new RenderArenaOwner();
            var ui = arena.BeginRender(new NoopRenderer(), view);
            var input = ui.Input(
                "検索"u8,
                new Utf8InputOptions("初期値"u8, "キーワード"u8, password: true)
            );

            arena.Validate(input);

            var stats = arena.GetStats();
            Assert.Equal(1, stats.Nodes);
            Assert.Equal("検索"u8.Length + "初期値"u8.Length + "キーワード"u8.Length + 2, stats.Utf8Bytes);
        }
        finally
        {
            view.DetachRuntime();
        }
    }

    [Fact]
    public void InputEventOwnsUtf8AndDecodesOnlyOnce()
    {
        var input = new InputEvent(InputEventKind.Changed, "こんにちは"u8.ToArray(), true, 7);

        Assert.True(input.Utf8Value.Span.SequenceEqual("こんにちは"u8));
        var first = input.Value;
        Assert.Same(first, input.Value);
        Assert.Equal("こんにちは", first);
        Assert.True(input.IsFocused);
        Assert.Equal(7UL, input.Revision);
    }

    [Fact]
    public void Utf8SetValueDoesNotAllocatePerCommand()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            var controller = new InputController(view, "search");
            controller.SetValue("warmup"u8);

            var before = GC.GetAllocatedBytesForCurrentThread();
            controller.SetValue("allocation-free UTF-8"u8);
            var after = GC.GetAllocatedBytesForCurrentThread();

            Assert.Equal(before, after);
        }
        finally
        {
            view.DetachRuntime();
        }
    }

    [Fact]
    public unsafe void Utf8OverlayConfigurationPassesManagedValidation()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            using var arena = new RenderArenaOwner();
            var ui = arena.BeginRender(new NoopRenderer(), view);
            var child = ui.Text("設定"u8);
            var overlay = ui
                .Overlay(
                    "settings"u8,
                    child,
                    new OverlayOptions(
                        placement: OverlayPlacement.Right,
                        margin: 24,
                        backdrop: Rgba(15, 23, 42, 128)
                    )
                )
                .OnDismiss(view.Click, 42);

            arena.Validate(overlay);

            var stats = arena.GetStats();
            Assert.Equal(2, stats.Nodes);
            Assert.Equal(1, stats.Children);
            Assert.Equal(8, stats.Ops);
            Assert.Equal("settings"u8.Length + "設定"u8.Length, stats.Utf8Bytes);

            arena.NativeArena->ChildLength = 0;
            Assert.Throws<InvalidOperationException>(() => arena.Validate(overlay));
        }
        finally
        {
            view.DetachRuntime();
        }
    }

    [Fact]
    public void OverlayOptionsRejectInvalidPlacementAndMargin()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OverlayOptions(placement: (OverlayPlacement)9)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() => new OverlayOptions(margin: -1));
    }

    private static void Attach(ProbeView view) =>
        view.AttachRuntime(
            1,
            static callback => callback(),
            static _ => { },
            static (_, _) => { },
            static (_, _, _) => { }
        );

    private sealed class ProbeView : View
    {
        internal Callback<ClickEvent> Click => BindClick(1);

        protected override Element Render(ref RenderContext ui) => ui.Div();
    }

    private sealed unsafe class NoopRenderer : IViewRenderer
    {
        public Element RenderChild<TView>(View parent, ChildSlot slot, Gpui.Interop.RenderArena* destination)
            where TView : View, IGeneratedViewFactory<TView> => throw new NotSupportedException();

        public Element RenderChild<TView, TProps>(View parent, ChildSlot slot, in TProps props, Gpui.Interop.RenderArena* destination)
            where TView : View<TProps>, IGeneratedViewFactory<TView> => throw new NotSupportedException();

        public Element RenderChild(View view, View parent, ChildSlot slot, Gpui.Interop.RenderArena* destination) =>
            throw new NotSupportedException();

        public Element RenderChild<TProps>(View<TProps> view, View parent, ChildSlot slot, in TProps props, Gpui.Interop.RenderArena* destination) =>
            throw new NotSupportedException();
    }
}
