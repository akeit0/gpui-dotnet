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
    public void WindowControlAreasPassManagedValidation()
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        var root = ui.HStack(
                ui.Text("Custom title bar"u8),
                ui.Button("minimize"u8, "—"u8)
                    .HoverBackground(Rgb(30, 41, 59))
                    .WindowControlArea(WindowControlArea.Minimize),
                ui.Button("close"u8, "×"u8)
                    .HoverBackground(Rgb(232, 17, 32))
                    .WindowControlArea(WindowControlArea.Close)
            )
            .WindowControlArea(WindowControlArea.Drag);

        arena.Validate(root);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            root.WindowControlArea((WindowControlArea)99)
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
            Assert.Equal(9, stats.Ops);
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

    [Fact]
    public unsafe void Utf8TooltipConfigurationPassesManagedValidation()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            using var arena = new RenderArenaOwner();
            var ui = arena.BeginRender(new NoopRenderer(), view);
            var tooltip = ui.Tooltip(
                "help"u8,
                ui.Button("help-button", "Help"u8),
                ui.Text("詳しい説明"u8),
                new TooltipOptions(
                    placement: TooltipPlacement.Top,
                    alignment: TooltipAlignment.End,
                    showDelay: TimeSpan.Zero,
                    hideDelay: TimeSpan.FromMilliseconds(125),
                    gap: 6,
                    margin: 10
                )
            );

            arena.Validate(tooltip);

            var stats = arena.GetStats();
            Assert.Equal(4, stats.Nodes);
            Assert.Equal(3, stats.Children);
            Assert.Equal(7, stats.Ops);
            Assert.Equal(
                "help"u8.Length + "help-button"u8.Length + "Help"u8.Length + "詳しい説明"u8.Length,
                stats.Utf8Bytes
            );

            arena.NativeArena->ChildLength = 2;
            Assert.Throws<InvalidOperationException>(() => arena.Validate(tooltip));
        }
        finally
        {
            view.DetachRuntime();
        }
    }

    [Fact]
    public void TooltipOptionsRejectInvalidGeometryAndTiming()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TooltipOptions(placement: (TooltipPlacement)5)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TooltipOptions(alignment: (TooltipAlignment)3)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TooltipOptions(showDelay: TimeSpan.FromMilliseconds(-1))
        );
        Assert.Throws<ArgumentOutOfRangeException>(() => new TooltipOptions(gap: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TooltipOptions(margin: float.NaN));
    }

    [Fact]
    public unsafe void Utf8ContextMenuConfigurationPassesManagedValidation()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            using var arena = new RenderArenaOwner();
            var ui = arena.BeginRender(new NoopRenderer(), view);
            var menu = ui.ContextMenu(
                "titlebar-menu"u8,
                ui.HStack(ui.Text("Title"u8)).Grow(),
                ui.VStack(
                    ui.Button("minimize"u8, "Minimize"u8),
                    ui.Button("close"u8, "Close"u8)
                ),
                new ContextMenuOptions(priority: 320, margin: 12)
            );

            arena.Validate(menu);

            var stats = arena.GetStats();
            Assert.Equal(8, stats.Nodes);
            Assert.Equal(7, stats.Children);
            Assert.Equal(6, stats.Ops);

            arena.NativeArena->ChildLength--;
            Assert.Throws<InvalidOperationException>(() => arena.Validate(menu));
        }
        finally
        {
            view.DetachRuntime();
        }
    }

    [Fact]
    public void ContextMenuOptionsRejectInvalidMargin()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContextMenuOptions(margin: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ContextMenuOptions(margin: float.NaN)
        );
    }

    [Fact]
    public void ExplicitListDatasourcePassesManagedValidation()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            using var arena = new RenderArenaOwner();
            var ui = arena.BeginRender(new NoopRenderer(), view);
            var list = ui.List("rows", new ListDataSource(100, (1UL << 40) | 42), view.Row);

            arena.Validate(list);

            var stats = arena.GetStats();
            Assert.Equal(1, stats.Nodes);
            Assert.Equal(10, stats.Ops);
        }
        finally
        {
            view.DetachRuntime();
        }
    }

    [Fact]
    public void ListDatasourceRejectsNegativeCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ListDataSource(-1, 0));
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
        internal ListItemRenderer Row => BindListRenderer(1);

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
