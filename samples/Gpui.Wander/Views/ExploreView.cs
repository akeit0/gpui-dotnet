using System.Runtime.CompilerServices;
using static Gpui.Units;

namespace Gpui;

internal readonly record struct ExploreProps(TravelStore Store);

/// <summary>
/// Home tab: stories rail, filter chips, virtual feed, bottom detail sheet.
/// Store notifications rebuild row content; a separate projection stamp resets native
/// positional state when visible identities change. Static handlers, stack spans, and an
/// inline stories buffer reduce allocations; rebuilding the memoized projection allocates.
/// </summary>
[GpuiView]
internal sealed partial class ExploreView : View<ExploreProps>
{
    [InlineArray(9)]
    private struct StoryBuffer
    {
        private Element _element;
    }

    private static readonly ListOptions FeedListOptions = new(
        batchSize: 32,
        overdraw: 480,
        estimatedItemHeight: 150
    );

    private static readonly ScrollOptions StoriesScrollOptions = new(
        showScrollbar: false
    );

    private static readonly string CoverPath = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "cover.svg"
    );

    private static readonly string[] StoryIds =
    [
        "story-all", "story-0", "story-1", "story-2", "story-3",
        "story-4", "story-5", "story-6", "story-7",
    ];

    private readonly Memo<ExploreFilter, List<FeedEntry>> _visible;
    private readonly Effect<ExploreProps> _watch;
    private readonly WorkScope _work;
    private InputController _search;
    private ListController _feed;
    private ulong _feedRevision = 1;
    private string _query = string.Empty;
    private int _chip;
    private int _storyDest = -1;
    private int _sheetDest = -1;
    private bool _refreshing;
    private List<FeedEntry> _rows = [];
    private TravelStore? _projectionStore;
    private ulong _projectionRevision = 1;

    public ExploreView(ViewConstruction construction, ExploreProps initialProps)
        : base(construction)
    {
        _visible = construction.Memo<ExploreFilter, List<FeedEntry>>();
        _work = construction.Work;
        _watch = construction.Effect<ExploreProps>(WatchStore);
    }

    private void WatchStore(EffectScope scope, ExploreProps input) =>
        scope.Own(input.Store.Subscribe(scope.Bind(this, static view => view.OnStoreChanged())));

    private void OnStoreChanged()
    {
        _feedRevision++;
        Invalidate();
    }

    private void SetQuery(string value)
    {
        _query = value;
        _feedRevision++;
        Invalidate();
    }

    private void SetChip(ulong payload)
    {
        _chip = checked((int)payload);
        _feedRevision++;
        Invalidate();
    }

    private void SetStory(ulong payload)
    {
        _storyDest = checked((int)payload) - 1;
        _feedRevision++;
        Invalidate();
    }

    private void ToggleLike(ulong payload) =>
        CommittedProps.Store.ToggleEntryLike(checked((long)payload));

    private void OpenSheet(ulong payload) => OpenDest(checked((int)payload));

    private void OpenDest(int destId)
    {
        _sheetDest = destId;
        Invalidate();
    }

    private void CloseSheet()
    {
        _sheetDest = -1;
        Invalidate();
    }

    private void ToggleDestLike(ulong payload) =>
        CommittedProps.Store.ToggleDestLike(checked((int)payload));

    private void AddToTrip(ulong payload) =>
        CommittedProps.Store.AddDestToTrip(checked((int)payload));

    private void Refresh()
    {
        if (_refreshing)
        {
            return;
        }
        _refreshing = true;
        _work.StartLatest(
            this,
            CommittedProps.Store.Entries.Count,
            static async (count, lifetime) =>
            {
                // No Task.Run: the delay never blocks a thread, so produce it inline.
                await Task.Delay(900, lifetime).ConfigureAwait(false);
                return count;
            },
            static (view, count) =>
            {
                var store = view.CommittedProps.Store;
                var authors = new[] { "Aiko", "Ben", "Chloe" };
                var texts = new[]
                {
                    "Just landed. The air smells like rain and grilled corn.",
                    "Overlook at golden hour did not disappoint.",
                    "New trail opened this week. Go early.",
                };
                view._refreshing = false;
                store.AddEntry(
                    authors[count % authors.Length],
                    texts[count % texts.Length],
                    count % store.Destinations.Count
                );
            },
            static (view, failure) =>
            {
                view._refreshing = false;
                view.Invalidate();
            }
        );
        Invalidate();
    }

    [GpuiListItem]
    private Element FeedRow(int index, in ExploreProps props, ref RenderContext ui)
    {
        var entry = _rows[index];
        var theme = ui.Theme;
        var dest = props.Store.FindDest(entry.DestId);
        return ui.Div(
                ui.HStack(
                        ui.Div()
                            .Width(Px(36))
                            .Height(Px(36))
                            .Radius(Px(18))
                            .Background(WanderStyles.AvatarColor(entry.Author.Length + entry.DestId, theme.Colors)),
                        ui.VStack(
                                ui.Text(entry.Author)
                                    .FontSize(Px(theme.Typography.BodySmall))
                                    .TextColor(theme.Colors.Text),
                                ui.Text($"{TimeLabel(entry.MinutesAgo)} · {dest?.Name ?? "Somewhere"}")
                                    .FontSize(Px(theme.Typography.Detail))
                                    .TextColor(theme.Colors.TextMuted)
                            )
                            .Gap(Px(0)),
                        ui.Spacer(),
                        ui.Badge(
                                ui.Text(WanderStyles.TagLabel(dest?.Tag ?? PlaceTag.City))
                                    .FontSize(Px(theme.Typography.Caption))
                            )
                            .Surface(new(theme.Colors.ElementActive, theme.Colors.TextMuted))
                            .Padding(Px(6))
                    )
                    .ItemsCenter()
                    .Gap(Px(10)),
                ui.Text(entry.Text)
                    .FontSize(Px(theme.Typography.Body))
                    .TextColor(theme.Colors.Text),
                ui.HStack(
                        ui.Button("like", $"{(entry.Liked ? "♥" : "♡")} {entry.Likes:N0}")
                            .OnClick(this, static (view, e) => view.ToggleLike(e.Payload), checked((ulong)entry.Id))
                            .Style(
                                WanderStyles.Button(
                                    theme,
                                    WanderButtonVariant.Like,
                                    entry.Liked
                                )
                            ),
                        ui.Button("view", "View place →")
                            .OnClick(this, static (view, e) => view.OpenSheet(e.Payload), checked((ulong)entry.DestId))
                            .Style(WanderStyles.Button(theme, WanderButtonVariant.Chip))
                    )
                    .Gap(Px(8))
            )
            .Gap(Px(10))
            .Padding(Px(14))
            .Background(theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.BorderVariant)
            .Radius(Px(16))
            .ItemId(checked((ulong)entry.Id));
    }

    private static string TimeLabel(int minutes) =>
        minutes switch
        {
            < 1 => "now",
            < 60 => $"{minutes}m",
            < 1440 => $"{minutes / 60}h",
            _ => $"{minutes / 1440}d",
        };

    protected override Element Render(in ExploreProps props, ref RenderContext ui)
    {
        ui.Effect(_watch, props);
        var theme = ui.Theme;
        var rows = _visible.Get(
            new ExploreFilter(props.Store, props.Store.Revision, new BoardQuery(_query), _chip, _storyDest),
            static input => TravelStore.ApplyFilter(input)
        );
        if (!ReferenceEquals(rows, _rows))
        {
            var sameOrder = ReferenceEquals(_projectionStore, props.Store) && rows.Count == _rows.Count;
            for (var index = 0; sameOrder && index < rows.Count; index++)
                sameOrder = rows[index].Id == _rows[index].Id;
            if (!sameOrder)
                _projectionRevision = checked(_projectionRevision + 1);
            _projectionStore = props.Store;
            _rows = rows;
        }

        var page = ui.VStack(
                ui.HStack(
                        ui.Input(ref _search, new Utf8InputOptions(placeholder: "Search places, people…"u8))
                            .Style(WanderStyles.Field(theme))
                            .OnChanged(this, static (view, e) => view.SetQuery(e.Value))
                            .Grow()
                            .MinWidth(Px(120)),
                        ui.Button("refresh-feed", _refreshing ? "…" : "Refresh")
                            .OnClick(this, static (view, _) => view.Refresh())
                            .Style(WanderStyles.Button(theme, WanderButtonVariant.Primary))
                    )
                    .Gap(Px(8))
                    .ItemsCenter(),
                StoriesRail(ref ui, props.Store),
                ChipsRow(ref ui),
                ui.List(
                        ref _feed,
                        // Local revision, not the store revision: row output depends on
                        // filter state too, and equal counts across filters must still
                        // evict (Mountains and Cities both yield 9 rows).
                        new ListDataSource(_rows.Count, _feedRevision, _projectionRevision),
                        Rows.FeedRow,
                        FeedListOptions
                    )
                    .Grow()
                    .Width(Percent(100))
            )
            .Gap(Px(12))
            .Grow();

        if (_sheetDest < 0)
        {
            return page;
        }
        var dest = props.Store.FindDest(_sheetDest);
        if (dest is null)
        {
            return page;
        }
        return ui.VStack(page, DestSheet(ref ui, dest)).Grow().Height(Percent(100));
    }

    private Element StoriesRail(ref RenderContext ui, TravelStore store)
    {
        var theme = ui.Theme;
        StoryBuffer buffer = default;
        buffer[0] = StoryItem(ref ui, this, theme, null, 0);
        var count = 1;
        var limit = Math.Min(store.Destinations.Count, StoryIds.Length - 1);
        for (var i = 0; i < limit; i++)
        {
            buffer[count++] = StoryItem(ref ui, this, theme, store.Destinations[i], i + 1);
        }
        Span<Element> stories = buffer;
        return ui.Scroll("stories", ScrollAxis.Horizontal, StoriesScrollOptions, ui.HStack(stories[..count]).Gap(Px(12)))
            .Width(Percent(100));
    }

    private static Element StoryItem(
        ref RenderContext ui,
        ExploreView view,
        GpuiTheme theme,
        Destination? dest,
        int payload
    )
    {
        var selected = view._storyDest == (dest?.Id ?? -1);
        var label = dest?.Name ?? "All";
        var circle = dest is null
            ? ui.Div(ui.Text("✈").TextColor(theme.Colors.SurfaceBackground))
                .Width(Px(56)).Height(Px(56)).Radius(Px(28))
                .Background(theme.Colors.Text).ItemsCenter().JustifyCenter()
            : ui.Div()
                .Width(Px(56)).Height(Px(56)).Radius(Px(28))
                .Background(WanderStyles.AvatarColor(dest.Id, theme.Colors))
                .BorderWidth(Px(selected ? 3 : 0))
                .BorderColor(theme.Colors.Accent);
        return ui.Button(StoryIds[payload], ui.VStack(circle, ui.Text(label)
                    .FontSize(Px(theme.Typography.Caption))
                    .TextColor(selected ? theme.Colors.TextAccent : theme.Colors.TextMuted))
                .Gap(Px(4))
                .ItemsCenter())
            .OnClick(view, static (v, e) => v.SetStory(e.Payload), checked((ulong)payload))
            .Padding(Px(2));
    }

    private Element ChipsRow(ref RenderContext ui)
    {
        var theme = ui.Theme;
        Span<Element> chips =
        [
            Chip(ref ui, this, theme, "All", 0),
            Chip(ref ui, this, theme, "Beach", 1),
            Chip(ref ui, this, theme, "Mountains", 2),
            Chip(ref ui, this, theme, "Cities", 3),
        ];
        return ui.HStack(chips).Gap(Px(8));
    }

    private static Element Chip(
        ref RenderContext ui,
        ExploreView view,
        GpuiTheme theme,
        string label,
        int chip
    ) =>
        ui.Button($"chip-{chip}", label)
            .OnClick(view, static (v, e) => v.SetChip(e.Payload), checked((ulong)chip))
            .Style(WanderStyles.Button(theme, WanderButtonVariant.Chip, view._chip == chip));

    private Element DestSheet(ref RenderContext ui, Destination dest)
    {
        var theme = ui.Theme;
        var panel = ui.VStack(
                ui.Image(CoverPath)
                    .Fit(ImageFit.Cover)
                    .Width(Percent(100))
                    .Height(Px(170))
                    .Radius(Px(14))
                    .Background(theme.Colors.ElementActive),
                ui.HStack(
                        ui.VStack(
                                ui.Text(dest.Name)
                                    .FontSize(Px(theme.Typography.Heading))
                                    .TextColor(theme.Colors.Text),
                                ui.Text($"{dest.Country} · {WanderStyles.TagLabel(dest.Tag)}")
                                    .FontSize(Px(theme.Typography.Detail))
                                    .TextColor(theme.Colors.TextMuted)
                            )
                            .Gap(Px(2)),
                        ui.Spacer(),
                        ui.Button("sheet-close", "Close")
                            .OnClick(this, static (view, _) => view.CloseSheet())
                            .Style(WanderStyles.Button(theme))
                    )
                    .ItemsCenter(),
                ui.Text(dest.Blurb)
                    .FontSize(Px(theme.Typography.BodySmall))
                    .TextColor(theme.Colors.Text),
                ui.HStack(
                        ui.Button("sheet-like", $"{(dest.Liked ? "♥" : "♡")} {dest.Likes:N0}")
                            .OnClick(this, static (view, e) => view.ToggleDestLike(e.Payload), checked((ulong)dest.Id))
                            .Style(WanderStyles.Button(theme, WanderButtonVariant.Like, dest.Liked)),
                        ui.Button("sheet-add", "＋ Add to current trip")
                            .OnClick(this, static (view, e) => view.AddToTrip(e.Payload), checked((ulong)dest.Id))
                            .Style(WanderStyles.Button(theme, WanderButtonVariant.Primary))
                    )
                    .Gap(Px(8))
                    .Wrap(FlexWrap.Wrap)
            )
            .Gap(Px(12))
            .Padding(Px(18))
            .Width(Percent(100))
            .Background(theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.Border)
            .Radius(Px(20));

        return ui.Sheet("dest-sheet", panel, SheetSide.Bottom)
            .OnDismiss(this, static (view, _) => view.CloseSheet());
    }
}
