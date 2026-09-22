using Gpui;
using Gpui.Components;
using Gpui.Editor;
using static Gpui.Units;

var hostName =
    OperatingSystem.IsWindows() ? "gpui_dotnet_components.dll"
    : OperatingSystem.IsMacOS() ? "libgpui_dotnet_components.dylib"
    : "libgpui_dotnet_components.so";
var application = new GpuiApplication(
    new NativeRuntimeOptions
    {
        LibraryPath = Path.Combine(AppContext.BaseDirectory, hostName),
        Extensions = [ComponentsExtension.Requirement, EditorExtension.Requirement],
    }
);
application.SetTheme(GpuiTheme.CreateDefault(GpuiThemeAppearance.Dark));
application.OpenWindow(
    ComponentsSampleView.Spec(),
    new GpuiWindowOptions
    {
        Title = "GPUI.NET Components Extension",
        Width = 980,
        Height = 720,
    }
);
application.Run();

[GpuiView]
internal sealed partial class ComponentsSampleView : View
{
    private uint _rating = 3;
    private uint _page = 3;
    private int _clicks;
    private bool _showAlert = true;
    private bool _switchChecked = true;
    private bool _checkboxChecked = true;
    private bool _radioChecked;
    private bool _toggleChecked;
    private readonly EditorController _editor;
    private readonly Effect<NoProps> _bootstrapEditor;

    public ComponentsSampleView(ViewConstruction context)
        : base(context)
    {
        _editor = context.CreateEditorController("catalog-editor");
        _bootstrapEditor = context.Effect<NoProps>(BootstrapEditor);
    }

    private void BootstrapEditor(EffectScope scope, NoProps input)
    {
        _editor.Bootstrap(
            "// Editor is retained by the same broad component host.\nfn main() {\n    println!(\"gpui-component\");\n}\n"
        );
    }

    protected override Element Render(ref RenderContext ui)
    {
        ui.Effect(_bootstrapEditor, default);
        var theme = ui.Theme;
        var button = ui.Button(
            "component-button",
            this,
            static (view, _) =>
            {
                view._clicks++;
                view.Invalidate();
            },
            new ComponentButtonOptions
            {
                Label = $"Native button · {_clicks}",
                Variant = ComponentButtonVariant.Primary,
            }
        );
        var rating = ui.Rating(
            "component-rating",
            this,
            static (view, changed) =>
            {
                view._rating = changed.Value;
                view.Invalidate();
            },
            new ComponentRatingOptions { Value = _rating }
        );
        Element alert = _showAlert
            ? ui.Alert(
                "component-alert",
                this,
                static (view, _) =>
                {
                    view._showAlert = false;
                    view.Invalidate();
                },
                new ComponentAlertOptions
                {
                    Variant = ComponentAlertVariant.Info,
                    Title = "Generated extension contract",
                    Message = "C# owns this composition; gpui-component owns native interaction.",
                }
            )
            : ui.Spacer();

        var additional = ui.GroupBox(
            "additional-components",
            new ComponentGroupBoxOptions
            {
                Title = "Broader official catalog",
                Variant = ComponentGroupBoxVariant.Outline,
            },
            ui.HStack(
                    ui.Avatar(
                        "catalog-avatar",
                        new ComponentAvatarOptions { Name = "GPUI Components" }
                    ),
                    ui.Label(
                        "catalog-label",
                        new ComponentLabelOptions
                        {
                            Text = "Generated semantic adapters",
                            Secondary = "22 families",
                            Highlight = "semantic",
                        }
                    ),
                    ui.Kbd("catalog-kbd", new ComponentKbdOptions { Keystroke = "cmd-shift-p" }),
                    ui.ShimmerText(
                            "catalog-shimmer",
                            new ComponentShimmerTextOptions { Text = "Native animation" }
                        )
                        .TextColor(theme.Colors.TextMuted)
                )
                .Gap(Px(16))
                .ItemsCenter()
                .Wrap(FlexWrap.Wrap),
            ui.Link(
                "catalog-link",
                this,
                static (view, _) =>
                {
                    view._clicks++;
                    view.Invalidate();
                },
                children: [ui.Text("Event-backed Link"u8)]
            ),
            ui.HStack(
                    ui.Switch(
                        "catalog-switch",
                        this,
                        static (view, changed) =>
                        {
                            view._switchChecked = changed.Value;
                            view.Invalidate();
                        },
                        new ComponentSwitchOptions { Checked = _switchChecked, Label = "Switch" }
                    ),
                    ui.Checkbox(
                        "catalog-checkbox",
                        this,
                        static (view, changed) =>
                        {
                            view._checkboxChecked = changed.Value;
                            view.Invalidate();
                        },
                        new ComponentCheckboxOptions
                        {
                            Checked = _checkboxChecked,
                            Label = "Checkbox",
                        }
                    ),
                    ui.Radio(
                        "catalog-radio",
                        this,
                        static (view, changed) =>
                        {
                            view._radioChecked = changed.Value;
                            view.Invalidate();
                        },
                        new ComponentRadioOptions { Checked = _radioChecked, Label = "Radio" }
                    ),
                    ui.Toggle(
                        "catalog-toggle",
                        this,
                        static (view, changed) =>
                        {
                            view._toggleChecked = changed.Value;
                            view.Invalidate();
                        },
                        new ComponentToggleOptions
                        {
                            Checked = _toggleChecked,
                            Label = "Toggle",
                            Variant = ComponentToggleVariant.Outline,
                        }
                    )
                )
                .Gap(Px(18))
                .ItemsCenter()
                .Wrap(FlexWrap.Wrap),
            ui.Pagination(
                "catalog-pagination",
                this,
                static (view, changed) =>
                {
                    view._page = changed.Page;
                    view.Invalidate();
                },
                new ComponentPaginationOptions
                {
                    CurrentPage = _page,
                    TotalPages = 12,
                    VisiblePages = 5,
                }
            ),
            ui.Collapsible(
                "catalog-collapsible",
                new ComponentCollapsibleOptions { Open = _switchChecked },
                ui.Text("Switch controls this native measured reveal."u8)
                    .Padding(Px(12))
                    .Background(theme.Colors.ElementBackground)
                    .Radius(Px(8))
            )
        );

        var body = ui.VStack(
                ui.Text("Optional gpui-component catalog"u8)
                    .FontSize(Px(theme.Typography.Heading))
                    .TextColor(theme.Colors.Text),
                alert,
                ui.GroupBox(
                    "status-group",
                    new ComponentGroupBoxOptions
                    {
                        Title = "Display and feedback",
                        Variant = ComponentGroupBoxVariant.Outline,
                    },
                    ui.HStack(
                            ui.Spinner("busy", new ComponentSpinnerOptions { Circular = true }),
                            ui.Badge(
                                "count",
                                new ComponentBadgeOptions { Count = 12 },
                                ui.Text("Inbox"u8)
                            ),
                            ui.Tag(
                                "status",
                                new ComponentTagOptions
                                {
                                    Variant = ComponentTagVariant.Success,
                                    RoundedFull = true,
                                },
                                ui.Text("Ready"u8)
                            )
                        )
                        .Gap(Px(20))
                        .ItemsCenter(),
                    ui.Separator(
                        "catalog-separator",
                        new ComponentSeparatorOptions { Label = "Retained controls" }
                    ),
                    ui.HStack(button, rating).Gap(Px(20)).ItemsCenter(),
                    ui.Progress(
                        "upload-progress",
                        new ComponentProgressOptions
                        {
                            Value = 68,
                            AccessibilityLabel = "Upload progress",
                        }
                    ),
                    ui.HStack(
                            ui.ProgressCircle(
                                "circle-progress",
                                new ComponentProgressCircleOptions { Value = 68, Diameter = 80 },
                                ui.HStack(ui.Text("68%"u8))
                                    .Width(Percent(100))
                                    .Height(Percent(100))
                                    .ItemsCenter()
                                    .JustifyCenter()
                            ),
                            ui.Skeleton(
                                "placeholder",
                                new ComponentSkeletonOptions { Secondary = true }
                            )
                        )
                        .Gap(Px(20))
                        .ItemsCenter(),
                    ui.Editor(_editor, new EditorOptions { Language = "rust", LineNumbers = true })
                        .Height(Px(180))
                        .Width(Percent(100))
                ),
                additional
            )
            .Gap(Px(18))
            .Padding(Px(24))
            .Width(Percent(100))
            .Background(theme.Colors.Background)
            .TextColor(theme.Colors.Text);

        var scroll = ui.Scroll(
                "component-catalog-scroll",
                ScrollAxis.Vertical,
                new ScrollOptions(
                    smoothScrolling: true,
                    showScrollbar: true,
                    scrollbarGutter: true
                ),
                body
            )
            .Grow()
            .Width(Percent(100))
            .Background(theme.Colors.Background);

        return ui.VStack(scroll).Grow().Width(Percent(100)).Background(theme.Colors.Background);
    }
}
