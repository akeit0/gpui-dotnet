using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class TableView : View
{
    private const int ItemCount = 5_000;
    private ListController _grid;
    private int _selected = -1;
    private string _activation = "Click or press Space to select; double-click or press Enter to activate";

    private void ActivateRow(ListActivationEvent e)
    {
        _activation = $"Activated svc-{e.Index:D4} via {e.Source} (ID {e.ItemId})";
        Invalidate();
    }

    private static readonly TableColumn[] Columns =
    [
        new("name", "Service", 0.45f, TableColumnWidth.Fraction),
        new("region", "Region", 130),
        new("status", "Status", 110, TableColumnWidth.Pixels, TableColumnAlignment.Center),
        new("rps", "Req/s", 90, TableColumnWidth.Pixels, TableColumnAlignment.Right),
    ];

    private void SelectRow(ListSelectionEvent e)
    {
        // This datasource has fixed ordering. Mutable models should resolve ItemId against their
        // current data before accepting a request from an earlier content revision.
        var index = e.Index;
        var previous = _selected;
        if (previous == index) return;
        _selected = index;
        if (previous >= 0)
        {
            _grid.RefreshRanges((previous, 1), (index, 1));
        }
        else
        {
            _grid.Refresh(index, 1);
        }
    }

    [GpuiListItem]
    private Element ServiceRow(int index, ref RenderContext ui)
    {
        var theme = ui.Theme;
        var colors = theme.Colors;
        var selected = index == _selected;
        var status =
            index % 11 == 0 ? "degraded"
            : index % 3 == 0 ? "draining"
            : "healthy";
        var statusColor =
            status == "healthy" ? colors.Success
            : status == "draining" ? colors.Warning
            : colors.Error;

        // Cells compose inside an explicit horizontal container: divs are block by default,
        // so the row must declare its own row layout. The cell widths reconcile against this
        // container, which stretches to the full row width.
        return ui.Div(
                ui.HStack(
                        ui.TableCell(
                            0,
                            ui.Text($"svc-{index:D4}")
                                .FontSize(Px(theme.Typography.BodySmall))
                        ),
                        ui.TableCell(
                            1,
                            ui.Text(Region(index))
                                .FontSize(Px(theme.Typography.Detail))
                                .TextColor(colors.TextMuted)
                        ),
                        ui.TableCell(2, ui.Text(status).TextColor(statusColor)),
                        ui.TableCell(
                            3,
                            ui.Text(Throughput(index))
                                .FontSize(Px(theme.Typography.Detail))
                                .TextColor(colors.TextMuted)
                        )
                    )
                    .Width(Percent(100))
            )
            .ItemId(checked((ulong)index) + 1)
            .Width(Percent(100))
            .Style(SampleStyles.CollectionRow(theme, selected));
    }

    protected override Element Render(ref RenderContext ui)
    {
        var theme = ui.Theme;
        var header = ui.HStack(
                ui.Text($"{ItemCount:N0} services")
                    .FontSize(Px(theme.Typography.Body))
                    .TextColor(theme.Colors.Text),
                ui.Spacer(),
                ui.Badge(ui.Text(_selected < 0 ? "select a row" : $"selected: svc-{_selected:D4}"))
                    .Background(theme.Colors.InfoBackground)
                    .TextColor(theme.Colors.Info)
                    .Padding(Px(7))
            )
            .ItemsCenter();

        var grid = ui.Table(
                ref _grid,
                new ListDataSource(ItemCount, 1),
                Rows.ServiceRow,
                new TableOptions(
                    batchSize: 64,
                    overdraw: 320,
                    estimatedItemHeight: 38,
                    scrollbarGutter: true
                ),
                Columns
            )
            .OnActivated(this, static (view, e) => view.ActivateRow(e))
            .OnSelectionRequested(this, static (view, e) => view.SelectRow(e))
            .Grow()
            .Width(Percent(100))
            .Background(theme.Colors.SurfaceBackground)
            .BorderColor(theme.Colors.BorderVariant)
            .BorderWidth(Px(1))
            .Radius(Px(8));

        return ui.VStack(header, ui.Text(_activation).TextColor(theme.Colors.TextMuted), grid).Gap(Px(10)).Grow();
    }

    private static string Region(int index) =>
        (index % 4) switch
        {
            0 => "eu-west",
            1 => "us-east",
            2 => "ap-south",
            _ => "us-west",
        };

    private static string Throughput(int index) => $"{index * 37 % 900 + 100:#,#}";
}
