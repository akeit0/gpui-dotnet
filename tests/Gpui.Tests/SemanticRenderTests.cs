using Gpui.Interop;
using static Gpui.Colors;
using static Gpui.Units;

namespace Gpui.Tests;

public sealed class SemanticRenderTests
{
    [Fact]
    public void RetainedFragmentRenderContextUsesTheSuppliedApplicationTheme()
    {
        var theme = GpuiTheme.CreateDefault(GpuiThemeAppearance.Dark);
        var view = new ProbeView();
        using var arena = new RenderArenaOwner();

        var ui = arena.BeginRender(new NoopRenderer(), view, theme);

        Assert.Same(theme, ui.Theme);
        Assert.Equal(Hex("#F8FAFC"), ui.Theme.Colors.Text);
    }

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
    public void ApplicationOwnedStyleWritesBaseAndInteractionOperations()
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        var style = new ProbeButtonStyle(
            Rgb(37, 99, 235),
            Rgb(29, 78, 216),
            Rgb(30, 64, 175),
            Rgb(255, 255, 255)
        );

        var root = ui.Button("styled", "Styled").Style(style);

        arena.Validate(root);
        Assert.Equal(
            style.ActiveBackground.Rgba,
            ReadLastU32Op(arena, OpCode.ActiveBackgroundRgba)
        );
    }

    [Fact]
    public void InteractiveElementCarriesItsManagedViewOwner()
    {
        var view = new ProbeView();
        Attach(view, 17);
        try
        {
            using var arena = new RenderArenaOwner();
            var ui = arena.BeginRender(new NoopRenderer(), view);
            var button = ui.Button("increment", "Increment");

            arena.Validate(button);

            Assert.Equal(17u, ReadLastU32Op(arena, OpCode.ElementOwner));
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Fact]
    public void FoundationControlFamilyCarriesControlledAndDisabledState()
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();

        var button = ui.Button("button", "Button").Disabled(true);
        arena.Validate(button);
        Assert.Equal(1u, ReadLastU32Op(arena, OpCode.Disabled));

        ui = arena.BeginRender();
        var checkbox = ui.Checkbox("checkbox", "Checkbox").Checked(true).Disabled(true);
        arena.Validate(checkbox);
        Assert.Equal(1u, ReadLastU32Op(arena, OpCode.Checked));
        Assert.Equal(1u, ReadLastU32Op(arena, OpCode.Disabled));

        ui = arena.BeginRender();
        var radio = ui.Radio("radio", "Radio").Checked(true).Disabled(false);
        arena.Validate(radio);
        Assert.Equal(1u, ReadLastU32Op(arena, OpCode.Checked));
        Assert.Equal(0u, ReadLastU32Op(arena, OpCode.Disabled));
    }

    [Fact]
    public void ElementOnlyInteractiveElementDoesNotRequireAMountedView()
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        var button = ui.Button("increment", "Increment");

        arena.Validate(button);

        Assert.False(ContainsOp(arena, OpCode.ElementOwner));
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

        Assert.Throws<ArgumentOutOfRangeException>(() => image.Fit((ImageFit)99));
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
    public void SharedMenusRenderThroughTheLibraryTitleBar()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            using var arena = new RenderArenaOwner();
            var ui = arena.BeginRender(new NoopRenderer(), view);
            var menus = new[]
            {
                new GpuiMenu(
                    "File",
                    GpuiMenuItem.Command("Open", view.RecordClick),
                    GpuiMenuItem.Submenu(
                        new GpuiMenu("Recent", GpuiMenuItem.Command("Example", view.RecordClick))
                    ),
                    GpuiMenuItem.Separator()
                ),
            };

            var titleBar = GpuiTitleBar.Render(
                ref ui,
                "Application"u8,
                menus,
                new GpuiTitleBarOptions(forceManagedMenuOnMac: true)
            );

            arena.Validate(titleBar);
            Assert.True(arena.GetStats().Nodes > 0);
        }
        finally
        {
            view.UnmountRuntime();
        }
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
            Assert.Equal(
                "検索"u8.Length + "初期値"u8.Length + "キーワード"u8.Length + 2,
                stats.Utf8Bytes
            );
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Fact]
    public unsafe void RefBoundInputControllerGetsAStableAutoKey()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            using var arena = new RenderArenaOwner();
            InputController controller = default;
            Assert.False(controller.IsBound);

            var firstUi = arena.BeginRender(new NoopRenderer(), view);
            var first = firstUi.Input(
                ref controller,
                new Utf8InputOptions(placeholder: "Search"u8)
            );
            arena.Validate(first);

            Assert.True(controller.IsBound);
            var firstKey = ReadNodeKey(arena);
            Assert.True(firstKey.AsSpan().SequenceEqual(ResourceKeys.EncodeAutoKey(1)));

            var secondUi = arena.BeginRender(new NoopRenderer(), view);
            var second = secondUi.Input(
                ref controller,
                new Utf8InputOptions(placeholder: "Changed"u8)
            );
            arena.Validate(second);

            var secondKey = ReadNodeKey(arena);
            Assert.True(firstKey.AsSpan().SequenceEqual(secondKey));
            Assert.True(controller.Utf8KeySpan.SequenceEqual(firstKey));
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Fact]
    public async Task SliderConfigurationAndEventsPassManagedValidation()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            using var arena = new RenderArenaOwner();
            var ui = arena.BeginRender(new NoopRenderer(), view);
            var slider = ui.Slider(
                    "volume"u8,
                    new SliderOptions(
                        min: 10,
                        max: 110,
                        step: 5,
                        value: 35,
                        axis: SliderAxis.Vertical,
                        scale: SliderScale.Logarithmic
                    )
                )
                .OnChanged(view, (owner, input) => owner.SliderEvents += input.End)
                .OnReleased(view, (owner, input) => owner.SliderReleases += input.End);

            arena.Validate(slider);

            Assert.Equal(1, arena.GetStats().Nodes);
            Assert.True(arena.GetStats().Ops >= 6);

            var changed = ReadCallbackEventId(arena, OpCode.SliderOnChanged);
            var released = ReadCallbackEventId(arena, OpCode.SliderOnReleased);
            await view.DispatchSliderCore(
                changed,
                new SliderEvent(SliderEventKind.Changed, 50, 50, false, 4)
            );
            await view.DispatchSliderCore(
                released,
                new SliderEvent(SliderEventKind.Released, 55, 55, false, 5)
            );

            Assert.Equal(50, view.SliderEvents);
            Assert.Equal(55, view.SliderReleases);
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Fact]
    public void SliderOptionsRejectInvalidRangesAndScales()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SliderOptions(min: 10, max: 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SliderOptions(step: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SliderOptions(min: 0, scale: SliderScale.Logarithmic)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() => new SliderOptions(axis: (SliderAxis)99));
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
            view.UnmountRuntime();
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
            var overlay = ui.Overlay(
                    "settings"u8,
                    child,
                    new OverlayOptions(
                        placement: OverlayPlacement.Right,
                        margin: 24,
                        backdrop: Rgba(15, 23, 42, 128)
                    )
                )
                .OnDismiss(view, (owner, _) => owner.RecordClick(), 42);

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
            view.UnmountRuntime();
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
    public unsafe void DialogAndSheetNormalizeOverlaySemantics()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            using (var dialogArena = new RenderArenaOwner())
            {
                var ui = dialogArena.BeginRender(new NoopRenderer(), view);
                var dialog = ui.Dialog(
                    "dialog"u8,
                    ui.Text("Dialog"u8),
                    new OverlayOptions(placement: OverlayPlacement.Right, modal: false)
                );

                dialogArena.Validate(dialog);
                Assert.Equal((ulong)OverlayPlacement.Center, dialogArena.NativeArena->Ops[1].A);
                Assert.Equal(1UL, dialogArena.NativeArena->Ops[4].A);
            }

            using (var sheetArena = new RenderArenaOwner())
            {
                var ui = sheetArena.BeginRender(new NoopRenderer(), view);
                var sheet = ui.Sheet(
                    "sheet"u8,
                    ui.Text("Sheet"u8),
                    SheetSide.Left,
                    new OverlayOptions(placement: OverlayPlacement.Center, modal: false)
                );

                sheetArena.Validate(sheet);
                Assert.Equal((ulong)OverlayPlacement.Left, sheetArena.NativeArena->Ops[1].A);
                Assert.Equal(1UL, sheetArena.NativeArena->Ops[4].A);
            }
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Fact]
    public void SheetRejectsInvalidSide()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            using var arena = new RenderArenaOwner();
            var ui = arena.BeginRender(new NoopRenderer(), view);
            var threw = false;
            try
            {
                _ = ui.Sheet("sheet"u8, ui.Text("Sheet"u8), (SheetSide)4);
            }
            catch (ArgumentOutOfRangeException)
            {
                threw = true;
            }
            Assert.True(threw);
        }
        finally
        {
            view.UnmountRuntime();
        }
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
            Assert.Equal(8, stats.Ops);
            Assert.Equal(
                "help"u8.Length + "help-button"u8.Length + "Help"u8.Length + "詳しい説明"u8.Length,
                stats.Utf8Bytes
            );

            arena.NativeArena->ChildLength = 2;
            Assert.Throws<InvalidOperationException>(() => arena.Validate(tooltip));
        }
        finally
        {
            view.UnmountRuntime();
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
                ui.VStack(ui.Button("minimize"u8, "Minimize"u8), ui.Button("close"u8, "Close"u8)),
                new ContextMenuOptions(priority: 320, margin: 12)
            );

            arena.Validate(menu);

            var stats = arena.GetStats();
            Assert.Equal(8, stats.Nodes);
            Assert.Equal(7, stats.Children);
            Assert.Equal(8, stats.Ops);

            arena.NativeArena->ChildLength--;
            Assert.Throws<InvalidOperationException>(() => arena.Validate(menu));
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Fact]
    public void ContextMenuOptionsRejectInvalidMargin()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContextMenuOptions(margin: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContextMenuOptions(margin: float.NaN));
    }

    [Fact]
    public unsafe void Utf8PopoverMenuConfigurationPassesManagedValidation()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            using var arena = new RenderArenaOwner();
            var ui = arena.BeginRender(new NoopRenderer(), view);
            var menu = ui.PopoverMenu(
                "file-menu"u8,
                ui.Button("file-trigger"u8, "File"u8),
                ui.VStack(
                    ui.Button("new-window"u8, "New window"u8),
                    ui.Button("close-window"u8, "Close window"u8)
                ),
                new PopoverMenuOptions(priority: 325, margin: 10)
            );

            arena.Validate(menu);

            var stats = arena.GetStats();
            Assert.Equal(8, stats.Nodes);
            Assert.Equal(7, stats.Children);
            Assert.Equal(7, stats.Ops);

            arena.NativeArena->ChildLength--;
            Assert.Throws<InvalidOperationException>(() => arena.Validate(menu));
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Fact]
    public void PopoverMenuOptionsRejectInvalidMargin()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PopoverMenuOptions(margin: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PopoverMenuOptions(margin: float.NaN));
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
            Assert.Equal(4, stats.Ops);
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Fact]
    public unsafe void RefBoundListControllerGetsAStableAutoKey()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            using var arena = new RenderArenaOwner();
            ListController controller = default;
            Assert.False(controller.IsBound);

            var firstUi = arena.BeginRender(new NoopRenderer(), view);
            var first = firstUi.List(ref controller, new ListDataSource(100, 1), view.Row);
            arena.Validate(first);

            Assert.True(controller.IsBound);
            var firstKey = ReadNodeKey(arena);
            Assert.True(firstKey.AsSpan().SequenceEqual(ResourceKeys.EncodeAutoKey(1)));

            var secondUi = arena.BeginRender(new NoopRenderer(), view);
            var second = secondUi.List(ref controller, new ListDataSource(100, 2), view.Row);
            arena.Validate(second);

            var secondKey = ReadNodeKey(arena);
            Assert.True(firstKey.AsSpan().SequenceEqual(secondKey));
            Assert.True(controller.Utf8KeySpan.SequenceEqual(firstKey));
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Fact]
    public unsafe void RefBoundListControllerCanDeclareATable()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            using var arena = new RenderArenaOwner();
            ListController controller = default;
            var ui = arena.BeginRender(new NoopRenderer(), view);
            var table = ui.Table(
                ref controller,
                new ListDataSource(10, 1),
                view.Row,
                new TableColumn("name", "Name", 120)
            );

            arena.Validate(table);

            var key = ReadNodeKey(arena);
            Assert.True(key.AsSpan().SequenceEqual(ResourceKeys.EncodeAutoKey(1)));
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Fact]
    public void ListItemIdPassesManagedValidation()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            using var arena = new RenderArenaOwner();
            var ui = arena.BeginRender(new NoopRenderer(), view);
            var button = ui.Button("row", "label")
                .ItemId((1UL << 40) | 7)
                .OnClick(view, (owner, _) => owner.RecordClick());

            arena.Validate(button);

            var stats = arena.GetStats();
            Assert.Equal(2, stats.Nodes);
            Assert.Equal(3, stats.Ops);
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Fact]
    public void ListItemIdRejectsReservedZero()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                using var arena = new RenderArenaOwner();
                var ui = arena.BeginRender(new NoopRenderer(), view);
                ui.Button("row", "label").ItemId(0);
            });
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Fact]
    public async Task ViewBoundClickCallbackReusesAndUnregistersItsEntry()
    {
        var view = new DynamicCallbackView();
        Attach(view);
        try
        {
            using var arena = new RenderArenaOwner();

            var firstUi = arena.BeginRender(new NoopRenderer(), view);
            var first = view.RenderCore(ref firstUi);
            arena.Validate(first);
            var firstEventId = ReadCallbackEventId(arena);

            var secondUi = arena.BeginRender(new NoopRenderer(), view);
            var second = view.RenderCore(ref secondUi);
            arena.Validate(second);
            var secondEventId = ReadCallbackEventId(arena);

            Assert.Equal(firstEventId, secondEventId);
            await view.DispatchClickCore(firstEventId, default);
            Assert.Equal(1, view.ClickCount);

            view.BindCallback = false;
            var thirdUi = arena.BeginRender(new NoopRenderer(), view);
            var third = view.RenderCore(ref thirdUi);
            arena.Validate(third);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                view.DispatchClickCore(firstEventId, default).AsTask()
            );

            view.BindCallback = true;
            var fourthUi = arena.BeginRender(new NoopRenderer(), view);
            var fourth = view.RenderCore(ref fourthUi);
            arena.Validate(fourth);
            Assert.Equal(firstEventId, ReadCallbackEventId(arena));
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Fact]
    public async Task ViewBoundCallbackCanTargetAnotherMountedView()
    {
        var owner = new DynamicCallbackView();
        var target = new CallbackTargetView();
        Attach(owner, 1);
        Attach(target, 2);
        try
        {
            using var arena = new RenderArenaOwner();
            owner.CallbackTarget = target;
            var ui = arena.BeginRender(new NoopRenderer(), owner);
            var button = owner.RenderCore(ref ui);
            arena.Validate(button);

            var eventId = ReadCallbackEventId(arena);
            await owner.DispatchClickCore(eventId, default);
            Assert.Equal(1, target.ClickCount);
        }
        finally
        {
            target.UnmountRuntime();
            owner.UnmountRuntime();
        }
    }

    [Fact]
    public void ExplicitTablePassesManagedValidation()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            using var arena = new RenderArenaOwner();
            var ui = arena.BeginRender(new NoopRenderer(), view);
            var table = ui.Table(
                "grid",
                new ListDataSource(100, 7),
                view.Row,
                new TableOptions(batchSize: 48, overdraw: 240),
                new TableColumn("name", "Name", 120),
                new TableColumn(
                    "size",
                    "Size",
                    0.25f,
                    TableColumnWidth.Fraction,
                    TableColumnAlignment.Right
                )
            );

            arena.Validate(table);

            var stats = arena.GetStats();
            Assert.Equal(1, stats.Nodes);
            // 12 fixed ops + one packed column record op per declared column.
            Assert.Equal(4 + 2, stats.Ops);
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Fact]
    public void ManagedValidatorRejectsMalformedTableColumnRecords()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            using var arena = new RenderArenaOwner();
            var ui = arena.BeginRender(new NoopRenderer(), view);
            var table = ui.Table(
                "grid",
                new ListDataSource(10, 0),
                view.Row,
                new TableColumn("n", "Name", 120)
            );

            // Undefined alignment (3) inside the packed record must fail managed validation
            // exactly like the native validator would.
            ArenaWriter.AddU64(
                table.Inner,
                OpCode.TableColumn,
                BitConverter.SingleToUInt32Bits(120f) | (3UL << 34)
            );

            Assert.Throws<InvalidOperationException>(() => arena.Validate(table));
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Fact]
    public void TableRejectsInvalidColumns()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            Assert.Throws<ArgumentException>(() =>
            {
                using var arena = new RenderArenaOwner();
                var ui = arena.BeginRender(new NoopRenderer(), view);
                ui.Table(
                    "grid",
                    new ListDataSource(10, 0),
                    view.Row,
                    new TableColumn("", "Name", 120)
                );
            });
            Assert.Throws<ArgumentException>(() =>
            {
                using var arena = new RenderArenaOwner();
                var ui = arena.BeginRender(new NoopRenderer(), view);
                ui.Table(
                    "grid",
                    new ListDataSource(10, 0),
                    view.Row,
                    new TableColumn("n", "Name", 1.4f, TableColumnWidth.Fraction)
                );
            });
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Fact]
    public void TableRejectsDuplicateColumnKeysAndUndefinedEnums()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            Assert.Throws<ArgumentException>(() =>
            {
                using var arena = new RenderArenaOwner();
                var ui = arena.BeginRender(new NoopRenderer(), view);
                ui.Table(
                    "grid",
                    new ListDataSource(10, 0),
                    view.Row,
                    new TableColumn("n", "Name", 120),
                    new TableColumn("n", "Name2", 120)
                );
            });
            Assert.Throws<ArgumentException>(() =>
            {
                using var arena = new RenderArenaOwner();
                var ui = arena.BeginRender(new NoopRenderer(), view);
                ui.Table(
                    "grid",
                    new ListDataSource(10, 0),
                    view.Row,
                    new TableColumn(
                        "n",
                        "Name",
                        120,
                        TableColumnWidth.Pixels,
                        (TableColumnAlignment)99
                    )
                );
            });
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Fact]
    public void GeneratedFactoriesRejectEmptyRequiredIds()
    {
        Assert.Throws<ArgumentException>(ui_ButtonEmpty);
        Assert.Throws<ArgumentException>(ui_CheckboxEmpty);
        Assert.Throws<ArgumentException>(ui_RadioEmpty);
        Assert.Throws<ArgumentException>(ui_ButtonEmptyUtf8);
    }

    private static object ui_ButtonEmpty()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            using var arena = new RenderArenaOwner();
            var ui = arena.BeginRender(new NoopRenderer(), view);
            return ui.Button("", "x");
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    private static object ui_ButtonEmptyUtf8()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            using var arena = new RenderArenaOwner();
            var ui = arena.BeginRender(new NoopRenderer(), view);
            return ui.Button(Array.Empty<byte>(), "x");
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    private static object ui_CheckboxEmpty()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            using var arena = new RenderArenaOwner();
            var ui = arena.BeginRender(new NoopRenderer(), view);
            return ui.Checkbox("", "x");
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    private static object ui_RadioEmpty()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            using var arena = new RenderArenaOwner();
            var ui = arena.BeginRender(new NoopRenderer(), view);
            return ui.Radio("", "x");
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Fact]
    public void TableCellPassesManagedValidation()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            using var arena = new RenderArenaOwner();
            var ui = arena.BeginRender(new NoopRenderer(), view);
            var cell = ui.TableCell(1, ui.Text("value"));

            arena.Validate(cell);

            var stats = arena.GetStats();
            Assert.Equal(2, stats.Nodes);
            Assert.Equal(1, stats.Ops);
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Fact]
    public void TableCellRejectsNegativeColumn()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                using var arena = new RenderArenaOwner();
                var ui = arena.BeginRender(new NoopRenderer(), view);
                ui.TableCell(-1, ui.Text("value"));
            });
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Fact]
    public void ListDatasourceRejectsNegativeCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ListDataSource(-1, 0));
    }

    [Fact]
    public void ListRefreshRangesValidatesBeforeDispatch()
    {
        ListController controller = default;

        Assert.Throws<ArgumentException>(() => controller.RefreshRanges());
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.RefreshRanges((0, 1), (-1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.RefreshRanges((0, -1)));
        // An unmounted controller still rejects the command only after argument validation.
        Assert.Throws<InvalidOperationException>(() => controller.RefreshRanges((0, 1)));
    }

    [Fact]
    public unsafe void DrawingPathsUseCompactValidatedGeometryOperations()
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        var area = ui.Path()
            .MoveTo(0, 100)
            .LineTo(25, 55)
            .QuadraticTo(40, 35, 50, 70)
            .CubicTo(65, 85, 72, 15, 100, 40)
            .LineTo(100, 100)
            .Close()
            .Fill(Rgba(37, 99, 235, 48), PathFillRule.EvenOdd);
        var line = ui.Path()
            .MoveTo(0, 100)
            .LineTo(25, 55)
            .ArcTo(10, 8, 0, false, true, 50, 70)
            .LineTo(100, 40)
            .Stroke(Rgb(37, 99, 235), Px(2))
            .Dash(Px(6), Px(3));
        var drawing = ui.Drawing(area, line).ViewBox(0, 0, 100, 100).Height(Px(240));

        arena.Validate(drawing);

        Assert.Equal(3, arena.GetStats().Nodes);
        Assert.Equal(2, arena.GetStats().Children);
        var move = FindOp(arena, OpCode.PathMoveTo);
        Assert.Equal(ValueKind.F32x2, (ValueKind)move.ValueKind);
        Assert.Equal(0, BitConverter.UInt32BitsToSingle((uint)move.A));
        Assert.Equal(100, BitConverter.UInt32BitsToSingle((uint)(move.A >> 32)));
    }

    [Fact]
    public void DrawingApiRejectsInvalidGeometryAtTheManagedBoundary()
    {
        Assert.Throws<ArgumentOutOfRangeException>(RenderInvalidMove);
        Assert.Throws<ArgumentOutOfRangeException>(RenderInvalidCircle);
        Assert.Throws<ArgumentOutOfRangeException>(RenderInvalidStroke);
        Assert.Throws<ArgumentOutOfRangeException>(RenderInvalidViewBox);
    }

    [Fact]
    public void CircleUsesUniformViewBoxPrimitiveOperations()
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        var circle = ui.Circle(38, 48, 2.5f).Fill(Rgb(37, 99, 235));
        var drawing = ui.Drawing(circle).ViewBox(0, 0, 100, 110);

        arena.Validate(drawing);

        var center = FindOp(arena, OpCode.PathCircleCenter);
        Assert.Equal(38, BitConverter.UInt32BitsToSingle((uint)center.A));
        Assert.Equal(48, BitConverter.UInt32BitsToSingle((uint)(center.A >> 32)));
        var radius = FindOp(arena, OpCode.PathCircleRadius);
        Assert.Equal(2.5f, BitConverter.UInt32BitsToSingle((uint)radius.A));
    }

    [Fact]
    public void PathMustBelongToADrawing()
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        var path = ui.Path().MoveTo(0, 0).LineTo(1, 1).Stroke(Rgb(0, 0, 0), Px(1));

        Assert.Throws<InvalidOperationException>(() => arena.Validate(path));
    }

    [Fact]
    public void DynamicWrapsOneElementWithItsOwnerAndActiveState()
    {
        var view = new ProbeView();
        Attach(view, 17);
        try
        {
            using var arena = new RenderArenaOwner();
            var ui = arena.BeginRender(new NoopRenderer(), view);
            var dynamic = ui.Dynamic(true, ui.Text("frame"u8));

            arena.Validate(dynamic);

            Assert.Equal(2, arena.GetStats().Nodes);
            Assert.Equal(1, arena.GetStats().Children);
            Assert.Equal(17u, ReadLastU32Op(arena, OpCode.ResourceOwner));
            Assert.Equal(1u, ReadLastU32Op(arena, OpCode.DynamicActive));
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Fact]
    public void DisabledDynamicWrapperDoesNotRequestFrames()
    {
        var view = new ProbeView();
        Attach(view);
        try
        {
            using var arena = new RenderArenaOwner();
            var ui = arena.BeginRender(new NoopRenderer(), view);
            var dynamic = ui.Dynamic(false, ui.Div());

            arena.Validate(dynamic);

            Assert.Equal(0u, ReadLastU32Op(arena, OpCode.DynamicActive));
        }
        finally
        {
            view.UnmountRuntime();
        }
    }

    [Fact]
    public void DynamicRequiresAMountedOwningView()
    {
        Assert.Throws<InvalidOperationException>(RenderDynamicWithoutOwner);
    }

    private static void Attach(View view, uint handle = 1) =>
        view.AttachRuntime(
            handle,
            static callback => callback(),
            static _ => { },
            static (_, _) => { },
            static (_, _, _) => { }
        );

    private static void RenderInvalidMove()
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        ui.Path().MoveTo(float.NaN, 0);
    }

    private static void RenderDynamicWithoutOwner()
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        ui.Dynamic(ui.Div());
    }

    private static void RenderInvalidCircle()
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        ui.Circle(0, 0, 0);
    }

    private static void RenderInvalidStroke()
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        ui.Path().MoveTo(0, 0).Stroke(Rgb(0, 0, 0), Px(0));
    }

    private static void RenderInvalidViewBox()
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        ui.Drawing(ui.Path()).ViewBox(0, 0, -1, 1);
    }

    private static unsafe uint ReadCallbackEventId(RenderArenaOwner arena) =>
        ReadCallbackEventId(arena, OpCode.OnClick);

    private static unsafe uint ReadCallbackEventId(RenderArenaOwner arena, OpCode code)
    {
        for (var index = 0; index < arena.NativeArena->OpLength; index++)
        {
            if (arena.NativeArena->Ops[index].Code == (ushort)code)
            {
                return checked((uint)(arena.NativeArena->Ops[index].A & uint.MaxValue));
            }
        }

        throw new Xunit.Sdk.XunitException("The render did not contain an OnClick operation.");
    }

    private static unsafe byte[] ReadNodeKey(RenderArenaOwner arena)
    {
        var node = arena.NativeArena->Nodes[0];
        var payload = new ReadOnlySpan<byte>(
            arena.NativeArena->Utf8 + node.DataOffset,
            checked((int)node.DataLength)
        );
        var separator = payload.IndexOf((byte)0);
        var keyLength = separator < 0 ? payload.Length : separator;
        Assert.True(keyLength > 0);
        return payload[..keyLength].ToArray();
    }

    private static unsafe uint ReadLastU32Op(RenderArenaOwner arena, OpCode code)
    {
        for (var index = arena.NativeArena->OpLength - 1; index >= 0; index--)
        {
            if (arena.NativeArena->Ops[index].Code == (ushort)code)
            {
                return checked((uint)arena.NativeArena->Ops[index].A);
            }
        }

        throw new Xunit.Sdk.XunitException($"The render did not contain {code}.");
    }

    private static unsafe bool ContainsOp(RenderArenaOwner arena, OpCode code)
    {
        for (var index = 0; index < arena.NativeArena->OpLength; index++)
        {
            if (arena.NativeArena->Ops[index].Code == (ushort)code)
            {
                return true;
            }
        }

        return false;
    }

    private static unsafe OpRecord FindOp(RenderArenaOwner arena, OpCode code)
    {
        for (var index = 0; index < arena.NativeArena->OpLength; index++)
        {
            if (arena.NativeArena->Ops[index].Code == (ushort)code)
            {
                return arena.NativeArena->Ops[index];
            }
        }

        throw new Xunit.Sdk.XunitException($"The render did not contain {code}.");
    }

    private readonly record struct ProbeButtonStyle(
        Color Background,
        Color HoverBackground,
        Color ActiveBackground,
        Color Text
    ) : IGpuiElementStyle<ButtonTag>
    {
        public Element<ButtonTag> Apply(Element<ButtonTag> button) =>
            button
                .Background(Background)
                .HoverBackground(HoverBackground)
                .ActiveBackground(ActiveBackground)
                .TextColor(Text);
    }

    private sealed class ProbeView : View
    {
        internal float SliderEvents;
        internal float SliderReleases;

        internal ListItemRenderer Row => BindListRenderer(1);

        internal void RecordClick() { }

        protected override Element Render(ref RenderContext ui) => ui.Div();
    }

    private sealed class DynamicCallbackView : View
    {
        internal bool BindCallback = true;
        internal CallbackTargetView? CallbackTarget;
        internal int ClickCount;

        protected override Element Render(ref RenderContext ui)
        {
            var button = ui.Button("dynamic", "dynamic");
            if (!BindCallback)
            {
                return button;
            }

            if (CallbackTarget is { } target)
            {
                return button.OnClick(target, (view, _) => view.ClickCount++);
            }

            return button.OnClick(this, (view, _) => view.ClickCount++);
        }
    }

    private sealed class CallbackTargetView : View
    {
        internal int ClickCount;

        protected override Element Render(ref RenderContext ui) => ui.Div();
    }

    private sealed unsafe class NoopRenderer : IViewRenderer
    {
        public Element RenderChild<TView>(
            ViewBase parent,
            ChildSlot slot,
            Gpui.Interop.RenderArena* destination
        )
            where TView : View, IGeneratedViewFactory<TView> => throw new NotSupportedException();

        public Element RenderChild<TView, TProps>(
            ViewBase parent,
            ChildSlot slot,
            in TProps props,
            Gpui.Interop.RenderArena* destination
        )
            where TProps : IEquatable<TProps>
            where TView : View<TProps>, IGeneratedViewFactory<TView> =>
            throw new NotSupportedException();
    }
}
