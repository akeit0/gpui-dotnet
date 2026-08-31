using Gpui;
using static Gpui.Colors;
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
        new(
            "status",
            "Status",
            110,
            TableColumnWidth.Pixels,
            TableColumnAlignment.Center
        ),
        new("rps", "Req/s", 90, TableColumnWidth.Pixels, TableColumnAlignment.Right),
    ];

    protected override void OnMounted(ref ViewContext context)
    {
        _grid = context.CreateListController("service-grid");
    }

    [GpuiEvent]
    private void SelectRow(ClickEvent e)
    {
        // The payload-less OnClick delivers the row's ItemId as its payload — the stable model
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
        var selected = index == _selected;
        var status =
            index % 11 == 0 ? "degraded"
            : index % 3 == 0 ? "draining"
            : "healthy";
        var statusColor =
            status == "healthy" ? Rgb(21, 128, 61)
            : status == "draining" ? Rgb(180, 83, 9)
            : Rgb(185, 28, 28);

        // Cells compose inside an explicit horizontal container: divs are block by default,
        // so the row must declare its own row layout. The cell widths reconcile against this
        // container, which stretches to the full row width.
        return ui.Button(
                "service-row",
                ui.HStack(
                        ui.TableCell(0, ui.Text($"svc-{index:D4}").TextColor(Rgb(15, 23, 42))),
                        ui.TableCell(1, ui.Text(Region(index)).TextColor(Rgb(71, 85, 105))),
                        ui.TableCell(2, ui.Text(status).TextColor(statusColor)),
                        ui.TableCell(3, ui.Text(Throughput(index)).TextColor(Rgb(51, 65, 85)))
                    )
                    .Width(Percent(100))
            )
            .ItemId(checked((ulong)index) + 1)
            .OnClick(Events.SelectRow)
            .Width(Percent(100))
            .Padding(Px(selected ? 12 : 9))
            .Background(selected ? Rgb(224, 231, 255) : Rgb(255, 255, 255))
            .BorderColor(selected ? Rgb(99, 102, 241) : Rgb(226, 232, 240))
            .BorderWidth(Px(1));
    }

    protected override Element Render(ref RenderContext ui)
    {
        var header = ui.HStack(
                ui.Text($"{ItemCount:N0} services")
                    .FontSize(Px(14))
                    .TextColor(Rgb(30, 41, 59)),
                ui.Spacer(),
                ui.Badge(
                        ui.Text(
                            _selected < 0 ? "select a row" : $"selected: svc-{_selected:D4}"
                        )
                    )
                    .Background(Rgb(224, 231, 255))
                    .TextColor(Rgb(67, 56, 202))
                    .Padding(Px(7))
            )
            .ItemsCenter();

        var grid = ui.Table(
                "service-grid",
                new ListDataSource(ItemCount, 1),
                Rows.ServiceRow,
                new TableOptions(batchSize: 64, overdraw: 320, estimatedItemHeight: 38),
                Columns
            )
            .Grow()
            .Width(Percent(100))
            .Background(Rgb(255, 255, 255))
            .BorderColor(Rgb(226, 232, 240))
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
