using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class ActivityView : View
{
    private const int ItemCount = 20_000;
    private ListController _list;
    private int _selected = -1;
    private bool _horizontal;

    private void SelectRow(ClickEvent e)
    {
        var next = checked((int)e.Payload);
        if (next == _selected)
        {
            return;
        }

        var previous = _selected;
        _selected = next;
        // Selection changes only these items, including measurements outside cached batches.
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
    private Element ActivityItem(int index, ref RenderContext ui)
    {
        var colors = ui.Theme.Colors;
        var selected = index == _selected;
        var heightHint = index % 7 == 0 ? "variable height" : "normal item";

        return ui.Button("activity-row", $"#{index:N0}  •  {heightHint}")
            .OnClick(this, (view, e) => view.SelectRow(e), checked((ulong)index))
            .Padding(
                Px(
                    selected ? 18
                    : index % 7 == 0 ? 14
                    : 9
                )
            )
            .Background(selected ? colors.ElementSelected : colors.SurfaceBackground)
            .BorderColor(selected ? colors.BorderSelected : colors.BorderVariant)
            .TextColor(selected ? colors.TextAccent : colors.Text);
    }

    protected override Element Render(ref RenderContext ui)
    {
        var theme = ui.Theme;
        var header = ui.HStack(
                ui.Text(
                        _horizontal
                            ? "Horizontal strip (Left/Right, Home/End, PageUp/PageDown)"
                            : $"{ItemCount:N0} virtual items"
                    )
                    .FontSize(Px(theme.Typography.Body))
                    .TextColor(theme.Colors.Text),
                ui.Spacer(),
                ui.Button(
                        "toggle-orientation",
                        _horizontal ? "Layout: Horizontal" : "Layout: Vertical"
                    )
                    .OnClick(this, static (view, _) => view.ToggleOrientation())
                    .Padding(Px(8)),
                ui.Button("jump-middle", "Jump to 10,000")
                    .OnClick(this, (view, _) => view._list.ScrollToItem(ItemCount / 2))
                    .Padding(Px(8))
            )
            .ItemsCenter();

        var list = ui.List(
                ref _list,
                // Range refreshes carry content changes; a revision bump would evict every batch.
                new ListDataSource(ItemCount, contentRevision: 0),
                Items.ActivityItem,
                new ListOptions(
                    batchSize: 48,
                    overdraw: 320,
                    // Vertical items use the native 40 px default; the strip seeds 220 px widths.
                    estimatedItemExtent: _horizontal ? (float?)220 : null,
                    smoothScrolling: true,
                    showScrollbar: true,
                    scrollbarGutter: true,
                    orientation: _horizontal ? ListOrientation.Horizontal : ListOrientation.Vertical
                )
            )
            .Grow()
            .Width(Percent(100));

        return ui.VStack(header, list).Gap(Px(10)).Grow();
    }

    private void ToggleOrientation()
    {
        _horizontal = !_horizontal;
        Invalidate();
    }
}
