using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class DockView : View
{
    private readonly HashSet<string> _closedPanels = new(StringComparer.Ordinal);

    protected override Element Render(ref RenderContext ui)
    {
        var theme = ui.Theme;
        var explorer = ui.VStack(
                ui.Text("Workspace"u8)
                    .FontSize(Px(theme.Typography.Title))
                    .TextColor(theme.Colors.Text),
                ui.Text("src"u8).TextColor(theme.Colors.TextMuted),
                ui.Text("  Gpui"u8).TextColor(theme.Colors.Text),
                ui.Text("  Gpui.Core"u8).TextColor(theme.Colors.Text),
                ui.Text("samples"u8).TextColor(theme.Colors.TextMuted),
                ui.Text("  Gpui.Sample"u8).TextColor(theme.Colors.Text)
            )
            .Gap(Px(7))
            .Padding(Px(12))
            .Grow()
            .Background(theme.Colors.SurfaceBackground);

        var alpha = new CounterCardProps("Docked editor A", 1);
        var beta = new CounterCardProps("Docked editor B", 1);
        var showA = !_closedPanels.Contains("editor-a");
        var showB = !_closedPanels.Contains("editor-b");
        Element center;
        if (showA && showB)
        {
            center = ui.DockTabs(
                panels:
                [
                    ui.DockPanel(
                        "editor-a",
                        "Editor A",
                        ui.Child<CounterCardView, CounterCardProps>("editor-a", in alpha)
                    ),
                    ui.DockPanel(
                        "editor-b",
                        "Editor B",
                        ui.Child<CounterCardView, CounterCardProps>("editor-b", in beta)
                    ),
                ]
            );
        }
        else if (showA)
        {
            center = ui.DockTabs(
                panels: ui.DockPanel(
                    "editor-a",
                    "Editor A",
                    ui.Child<CounterCardView, CounterCardProps>("editor-a", in alpha)
                )
            );
        }
        else if (showB)
        {
            center = ui.DockTabs(
                panels: ui.DockPanel(
                    "editor-b",
                    "Editor B",
                    ui.Child<CounterCardView, CounterCardProps>("editor-b", in beta)
                )
            );
        }
        else
        {
            center = ui.DockTabs(
                panels: ui.DockPanel("empty", "Empty", ui.Text("All panels closed."u8))
            );
        }
        var left = ui
            .DockRegion(
                DockSide.Left,
                ui.DockTabs(
                    panels: ui.DockPanel(
                        "explorer",
                        "Explorer",
                        explorer,
                        new DockPanelOptions(closable: false)
                    )
                )
            )
            .InitialSize(180);
        var bottom = ui
            .DockRegion(
                DockSide.Bottom,
                ui.DockTabs(
                    panels: ui.DockPanel(
                        "activity",
                        "Activity",
                        ui.Child<ActivityView>("activity")
                    )
                )
            )
            .InitialSize(220);
        var inspector = ui
            .VStack(
                ui.Text("Selection"u8).TextColor(theme.Colors.TextMuted),
                ui.Text("No symbol selected"u8).TextColor(theme.Colors.Text),
                ui.Divider(),
                ui.Text("Native side Dock"u8).TextColor(theme.Colors.TextMuted)
            )
            .Gap(Px(8))
            .Padding(Px(12))
            .Grow()
            .Background(theme.Colors.SurfaceBackground);
        var right = ui
            .DockRegion(
                DockSide.Right,
                ui.DockTabs(
                    panels: ui.DockPanel(
                        "inspector",
                        "Inspector",
                        inspector,
                        new DockPanelOptions(closable: false)
                    )
                )
            )
            .InitialSize(180);

        return ui
            .DockArea("sample-dock", center, [left, bottom, right])
            .OnDockPanelClosed(this, (view, dockEvent) => view.OnPanelClosed(dockEvent))
            .Grow()
            .Width(Percent(100))
            .Height(Percent(100))
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.BorderVariant);
    }

    private void OnPanelClosed(DockEvent dockEvent)
    {
        if (dockEvent.Kind == DockEventKind.PanelClosed && dockEvent.PanelId.Length != 0)
        {
            _closedPanels.Add(dockEvent.PanelId);
            Invalidate();
        }
    }
}
