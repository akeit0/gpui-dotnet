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
    private ComponentAttachmentStatus _fileStatus = ComponentAttachmentStatus.Uploading;
    private int _fileOpens;
    private bool _fileArchived;
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

        var fileAttachment = ui.Attachment(
            "catalog-file",
            this,
            static (view, _) =>
            {
                view._fileOpens++;
                view.Invalidate();
            },
            new ComponentAttachmentOptions
            {
                Title = "release-notes.pdf",
                Description = $"{_fileStatus} · opened {_fileOpens} times",
                Status = _fileStatus,
            },
            media: ui.Text("PDF"u8),
            content: ui.Progress(
                "file-progress",
                new ComponentProgressOptions
                {
                    Value = _fileStatus == ComponentAttachmentStatus.Complete ? 100 : 68,
                    AccessibilityLabel = "File upload progress",
                }
            ),
            actions: ui.HStack(
                    ui.Button(
                        "file-action",
                        this,
                        static (view, _) =>
                        {
                            view._fileStatus =
                                view._fileStatus == ComponentAttachmentStatus.Complete
                                    ? ComponentAttachmentStatus.Uploading
                                    : ComponentAttachmentStatus.Complete;
                            view.Invalidate();
                        },
                        new ComponentButtonOptions
                        {
                            Label =
                                _fileStatus == ComponentAttachmentStatus.Complete
                                    ? "Restart"
                                    : "Finish",
                            Variant = ComponentButtonVariant.Secondary,
                            Size = ComponentSize.Small,
                        }
                    ),
                    ui.Button(
                        "archive-action",
                        this,
                        static (view, _) =>
                        {
                            view._fileArchived = !view._fileArchived;
                            view.Invalidate();
                        },
                        new ComponentButtonOptions
                        {
                            Label = _fileArchived ? "Restore" : "Archive",
                            Variant = ComponentButtonVariant.Secondary,
                            Size = ComponentSize.Small,
                        }
                    )
                )
                .Gap(Px(8))
        );
        var fileRegion = _fileArchived
            ? ui.VStack(ui.Text("Archived files"u8), fileAttachment).Gap(Px(12))
            : ui.VStack(
                    fileAttachment,
                    ui.Empty(
                            "catalog-empty",
                            new ComponentEmptyOptions
                            {
                                Title = "No archived files",
                                Description = "Archive the file above to see it here.",
                                MediaVariant = ComponentEmptyMediaVariant.Icon,
                            },
                            media: ui.Icon(
                                "catalog-empty-icon",
                                new ComponentIconOptions
                                {
                                    AssetPath = "app-assets/Assets/archive-box.svg",
                                    Size = ComponentSize.Large,
                                }
                            ),
                            footer: ui.Text("Empty state presentation from gpui-component."u8)
                        )
                        .Height(Px(180))
                )
                .Gap(Px(16));

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
                            Secondary = "33 families",
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
            ui.Toolbar(
                "catalog-toolbar",
                new ComponentToolbarOptions { Size = ComponentSize.Small },
                ui.ToolbarGroup(
                    "catalog-toolbar-actions",
                    new ComponentToolbarGroupOptions { Label = "Document actions" },
                    ui.Button(
                        "toolbar-open",
                        this,
                        static (view, _) =>
                        {
                            view._clicks++;
                            view.Invalidate();
                        },
                        new ComponentButtonOptions { Label = "Open", Size = ComponentSize.Small }
                    ),
                    ui.Button(
                        "toolbar-save",
                        this,
                        static (view, _) =>
                        {
                            view._clicks++;
                            view.Invalidate();
                        },
                        new ComponentButtonOptions
                        {
                            Label = "Save",
                            Size = ComponentSize.Small,
                            IconAssetPath = "icons/save.svg",
                        }
                    )
                )
            ),
            fileRegion,
            ui.StatusBar(
                "catalog-status",
                left: ui.Text("Ready"u8),
                center: ui.Text($"Actions: {_clicks}"),
                right: ui.Text("UTF-8"u8)
            ),
            ui.MessageGroup(
                "catalog-conversation",
                ui.Message(
                    "catalog-message-in",
                    new ComponentMessageOptions { AccessibleListItem = true },
                    avatar: ui.Avatar(
                        "catalog-message-avatar",
                        new ComponentAvatarOptions { Name = "GPUI Kit" }
                    ),
                    header: ui.Text("GPUI Kit · 09:41"u8),
                    content: ui.BubbleGroup(
                        "catalog-bubble-group",
                        ui.Bubble(
                            "catalog-bubble-in",
                            new ComponentBubbleOptions
                            {
                                Variant = ComponentBubbleVariant.Secondary,
                            },
                            content: ui.Text("The file is ready for review."u8)
                        )
                    ),
                    footer: ui.Text("Received"u8)
                ),
                ui.Marker(
                    "catalog-marker",
                    new ComponentMarkerOptions
                    {
                        Variant = ComponentMarkerVariant.Separator,
                        Text = "Today",
                    }
                ),
                ui.Message(
                    "catalog-message-out",
                    new ComponentMessageOptions
                    {
                        Alignment = ComponentMessageAlignment.End,
                        AccessibleListItem = true,
                    },
                    content: ui.VStack(
                            ui.Bubble(
                                "catalog-bubble-out",
                                new ComponentBubbleOptions
                                {
                                    Variant = ComponentBubbleVariant.Filled,
                                    Alignment = ComponentMessageAlignment.End,
                                },
                                content: ui.Text("I’ll check the attachment."u8),
                                reactions: ui.Text("★ 2"u8)
                            )
                        )
                        .PaddingBottom(Px(20)),
                    footer: ui.Text("Sent"u8)
                )
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
