using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class TableView : View
{
    private const int ItemCount = 5_000;
    private ListController _grid;
    private int _selected = -1;
    private bool _descending;
    private ulong _revision = 1;
    private string _activation = "Click or press Space to select; double-click or press Enter to activate";

    private void ActivateRow(ListActivationEvent e)
    {
        if (e.ItemId is not { } id || id is 0 or > ItemCount) return;
        _activation = $"Activated svc-{id - 1:D4} via {e.Source} (ID {id})";
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
        if (e.ItemId is not { } id || id is 0 or > ItemCount) return;
        var index = checked((int)id - 1);
        var previous = _selected;
        if (previous == index) return;
        _selected = index;
        if (previous >= 0)
        {
            _grid.RefreshRanges((RowIndex(previous), 1), (RowIndex(index), 1));
        }
        else
        {
            _grid.Refresh(RowIndex(index), 1);
        }
    }

    [GpuiListItem]
    private Element ServiceRow(int index, ref RenderContext ui)
    {
        index = RowIndex(index);
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
                        ).PaddingX(Px(10)),
                        ui.TableCell(
                            1,
                            ui.Text(Region(index))
                                .FontSize(Px(theme.Typography.Detail))
                                .TextColor(colors.TextMuted)
                        ).PaddingX(Px(10)),
                        ui.TableCell(2, ui.Text(status).TextColor(statusColor)).PaddingX(Px(10)),
                        ui.TableCell(
                            3,
                            ui.Text(Throughput(index))
                                .FontSize(Px(theme.Typography.Detail))
                                .TextColor(colors.TextMuted)
                        ).PaddingX(Px(10))
                    )
                    .Width(Percent(100))
            )
            .ItemId(checked((ulong)index) + 1)
            .Width(Percent(100))
            .Style(SampleStyles.TableRow(theme, selected));
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
                new ListDataSource(ItemCount, _revision),
                Rows.ServiceRow,
                new TableOptions(
                    batchSize: 64,
                    overdraw: 320,
                    estimatedItemHeight: 38,
                    scrollbarGutter: true
                ),
                Columns
            )
            .Header(
                ui.Button("sort-service", _descending ? "Service ↓" : "Service ↑")
                    .Style(SampleStyles.TableHeader(theme))
                    .OnClick(this, static (view, _) => view.ToggleSort()),
                ui.Text("Region").TextColor(theme.Colors.TextMuted),
                ui.HStack(ui.Text("●").TextColor(theme.Colors.Success), ui.Text("Status"))
                    .Gap(Px(5)).ItemsCenter(),
                ui.Text("Req/s").TextColor(theme.Colors.TextMuted)
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

    private int RowIndex(int service) => _descending ? ItemCount - 1 - service : service;

    private void ToggleSort()
    {
        _descending = !_descending;
        _revision++;
        // Arbitrary reordering resets the native cursor/cache. Selection remains model-owned.
        _grid.Reset(ItemCount);
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
