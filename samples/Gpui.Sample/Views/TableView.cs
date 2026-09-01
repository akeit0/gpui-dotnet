using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class TableView : View
{
    private const int ItemCount = 5_000;
    private ListController _grid;
    private int _selected = -1;

    private static readonly TableColumn[] Columns =
    [
        new("name", "Service", 0.45f, TableColumnWidth.Fraction),
        new("region", "Region", 130),
        new("status", "Status", 110, TableColumnWidth.Pixels, TableColumnAlignment.Center),
        new("rps", "Req/s", 90, TableColumnWidth.Pixels, TableColumnAlignment.Right),
    ];

    private void SelectRow(ClickEvent e)
    {
        // An OnClick without an explicit payload delivers the row's ItemId as its payload — the stable model
        // identity that survives splices. Mapping an ID back to a refresh index is app-owned
        // datasource logic; here the ID is the row's original position + 1 by construction.
        var index = checked((int)e.Payload) - 1;
        var previous = _selected;
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
        return ui.Button(
                "service-row",
                ui.HStack(
                        ui.TableCell(
                            0,
                            ui.Text($"svc-{index:D4}")
                                .FontSize(Px(theme.Typography.BodySmall))
                                .TextColor(colors.Text)
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
            .OnClick(this, (view, e) => view.SelectRow(e))
            .Width(Percent(100))
            .Padding(Px(selected ? 12 : 9))
            .Background(selected ? colors.ElementSelected : colors.SurfaceBackground)
            .BorderColor(selected ? colors.BorderSelected : colors.BorderVariant)
            .TextColor(selected ? colors.TextAccent : colors.Text)
            .BorderWidth(Px(1));
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
            .Grow()
            .Width(Percent(100))
            .Background(theme.Colors.SurfaceBackground)
            .BorderColor(theme.Colors.BorderVariant)
            .BorderWidth(Px(1))
            .Radius(Px(8));

        return ui.VStack(header, grid).Gap(Px(10)).Grow();
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
