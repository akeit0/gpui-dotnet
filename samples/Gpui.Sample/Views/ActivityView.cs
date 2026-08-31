using Gpui;
using static Gpui.Colors;
using static Gpui.Units;

[GpuiView]
internal sealed partial class ActivityView : View
{
    private const int ItemCount = 20_000;
    private ListController _list;
    private int _selected = -1;
    private ulong _contentRevision;

    protected override void OnMounted(ref ViewContext context)
    {
        _list = context.CreateListController("activity-list");
    }

    [GpuiEvent]
    private void JumpToMiddle() => _list.ScrollToItem(ItemCount / 2);

    [GpuiEvent]
    private void SelectRow(ClickEvent e)
    {
        var next = checked((int)e.Payload);
        var previous = _selected;
        _selected = next;
        _contentRevision++;

        // The item count and ordering did not change. Refresh only the affected measurements
        // with one queued command; the controller also invalidates this View so the row
        // content is regenerated.
        if (previous >= 0)
        {
            _list.RefreshRanges((previous, 1), (next, 1));
        }
        else
        {
            _list.Refresh(next, 1);
        }
    }

    [GpuiListItem]
    private Element ActivityRow(int index, ref RenderContext ui)
    {
        var selected = index == _selected;
        var heightHint = index % 7 == 0 ? "variable height" : "normal row";

        return ui.Button("activity-row", $"#{index:N0}  •  {heightHint}")
            .OnClick(Events.SelectRow, checked((ulong)index))
            .Padding(
                Px(
                    selected ? 18
                    : index % 7 == 0 ? 14
                    : 9
                )
            )
            .Background(selected ? Rgb(224, 231, 255) : Rgb(255, 255, 255))
            .BorderColor(selected ? Rgb(99, 102, 241) : Rgb(226, 232, 240))
            .TextColor(selected ? Rgb(55, 48, 163) : Rgb(30, 41, 59));
    }

    protected override Element Render(ref RenderContext ui)
    {
        var header = ui.HStack(
                ui.Text($"{ItemCount:N0} virtual rows").FontSize(Px(14)).TextColor(Rgb(30, 41, 59)),
                ui.Spacer(),
                ui.Button("jump-middle", "Jump to 10,000")
                    .OnClick(Events.JumpToMiddle)
                    .Padding(Px(8))
            )
            .ItemsCenter();

        var list = ui.List(
                "activity-list",
                new ListDataSource(ItemCount, _contentRevision),
                Rows.ActivityRow,
                new ListOptions(
                    batchSize: 48,
                    overdraw: 320,
                    estimatedItemHeight: 40,
                    smoothScrolling: true,
                    showScrollbar: true,
                    scrollbarGutter: true
                )
            )
            .Grow()
            .Width(Percent(100));

        return ui.VStack(header, list).Gap(Px(10)).Grow();
    }
}
