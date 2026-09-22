using Gpui;
using Gpui.Components;
using static Gpui.Units;

var hostName =
    OperatingSystem.IsWindows() ? "gpui_dotnet_components.dll"
    : OperatingSystem.IsMacOS() ? "libgpui_dotnet_components.dylib"
    : "libgpui_dotnet_components.so";
var application = new GpuiApplication(
    new NativeRuntimeOptions
    {
        LibraryPath = Path.Combine(AppContext.BaseDirectory, hostName),
        Extensions = [ComponentsExtension.Requirement],
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
    private int _clicks;
    private bool _showAlert = true;

    public ComponentsSampleView(ViewConstruction context)
        : base(context) { }

    protected override Element Render(ref RenderContext ui)
    {
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

        return ui.VStack(
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
                                new ComponentProgressOptions { Value = 68 },
                                ui.Text("68%"u8)
                            ),
                            ui.Skeleton(
                                "placeholder",
                                new ComponentSkeletonOptions { Secondary = true }
                            )
                        )
                        .Gap(Px(20))
                        .ItemsCenter()
                )
            )
            .Gap(Px(18))
            .Padding(Px(24))
            .Width(Percent(100))
            .Height(Percent(100))
            .Background(theme.Colors.Background)
            .TextColor(theme.Colors.Text);
    }
}
