using System.Runtime.CompilerServices;
using static Gpui.Units;

namespace Gpui;

internal readonly record struct TripsProps(TravelStore Store);

/// <summary>
/// Trips tab: photo grid, bottom detail sheet with day checklist and star rating,
/// centered new-trip dialog. Dynamic trip count uses one inline buffer.
/// </summary>
[GpuiView]
internal sealed partial class TripsView : View<TripsProps>
{
    [InlineArray(7)]
    private struct CardBuffer
    {
        private Element _element;
    }

    [InlineArray(7)]
    private struct DayBuffer
    {
        private Element _element;
    }

    private static readonly string[] TripIds =
    [
        "trip-0", "trip-1", "trip-2", "trip-3", "trip-4", "trip-5", "trip-6",
    ];

    private static readonly string[] DayIds =
    [
        "trip-day-0", "trip-day-1", "trip-day-2", "trip-day-3",
        "trip-day-4", "trip-day-5", "trip-day-6",
    ];

    private static readonly string[] StarIds =
    [
        "trip-star-1", "trip-star-2", "trip-star-3", "trip-star-4", "trip-star-5",
    ];

    private static readonly string CoverPath = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "cover.svg"
    );

    private readonly Effect<NoProps> _watch;
    private int _selected = -1;
    private bool _dialog;
    private string _draft = string.Empty;
    private string _error = string.Empty;

    public TripsView(ViewConstruction construction, TripsProps initialProps)
        : base(construction) => _watch = construction.Effect<NoProps>(WatchStore);

    private void WatchStore(EffectScope scope, NoProps input) =>
        scope.Own(CommittedProps.Store.Subscribe(scope.Bind(this, static view => view.Invalidate())));

    private void SelectTrip(ulong payload)
    {
        _selected = checked((int)payload);
        Invalidate();
    }

    private void CloseSheet()
    {
        _selected = -1;
        Invalidate();
    }

    private void ToggleDay(ulong payload)
    {
        var packed = checked((int)payload);
        CommittedProps.Store.ToggleDay(packed >> 8, packed & 0xFF);
    }

    private void SetRating(ulong payload)
    {
        var packed = checked((int)payload);
        CommittedProps.Store.SetRating(packed >> 8, packed & 0xFF);
    }

    private void OpenDialog()
    {
        _draft = string.Empty;
        _error = string.Empty;
        _dialog = true;
        Invalidate();
    }

    private void CancelDialog()
    {
        _dialog = false;
        Invalidate();
    }

    private void SetDraft(string value) => _draft = value;

    private void CreateTrip()
    {
        if (string.IsNullOrWhiteSpace(_draft))
        {
            _error = "Name your trip first.";
            Invalidate();
            return;
        }
        CommittedProps.Store.AddTrip(_draft);
        _draft = string.Empty;
        _error = string.Empty;
        _dialog = false;
        Invalidate();
    }

    private static Element TripCard(
        ref RenderContext ui,
        TripsView view,
        GpuiTheme theme,
        Trip trip,
        int index
    )
    {
        var done = 0;
        foreach (var day in trip.Days)
        {
            if (day)
            {
                done++;
            }
        }
        return ui.Button(
                TripIds[index],
                ui.VStack(
                        ui.Div(ui.Text(trip.Title).TextColor(theme.Colors.SurfaceBackground))
                            .Padding(Px(10))
                            .Height(Px(76))
                            .Background(WanderStyles.AvatarColor(trip.Id, theme.Colors))
                            .Radius(Px(12)),
                        ui.Text($"{done}/{trip.Days.Length} days · {WanderStyles.Stars(trip.Rating)}")
                            .FontSize(Px(theme.Typography.Detail))
                            .TextColor(theme.Colors.TextMuted)
                    )
                    .Gap(Px(8))
            )
            .OnClick(view, static (v, e) => v.SelectTrip(e.Payload), checked((ulong)index))
            .Style(WanderStyles.Button(theme))
            .Width(Percent(100));
    }

    protected override Element Render(in TripsProps props, ref RenderContext ui)
    {
        ui.Effect(_watch, default);
        var theme = ui.Theme;
        var store = props.Store;

        CardBuffer cards = default;
        var count = 0;
        var limit = Math.Min(store.Trips.Count, TripIds.Length);
        for (var i = 0; i < limit; i++)
        {
            cards[count++] = TripCard(ref ui, this, theme, store.Trips[i], i);
        }
        Span<Element> cardSpan = cards;
        var page = ui.VStack(
                ui.HStack(
                        ui.Text("Your trips")
                            .FontSize(Px(theme.Typography.Heading))
                            .TextColor(theme.Colors.Text),
                        ui.Spacer(),
                        ui.Button("new-trip", "＋ New")
                            .OnClick(this, static (view, _) => view.OpenDialog())
                            .Style(WanderStyles.Button(theme, WanderButtonVariant.Primary))
                    )
                    .ItemsCenter(),
                ui.Div(cardSpan[..count])
                    .Grid()
                    .GridCols(2)
                    .Gap(Px(10))
                    .Width(Percent(100))
            )
            .Gap(Px(12))
            .Grow();

        if (_dialog)
        {
            return ui.VStack(page, NewTripDialog(ref ui)).Grow().Height(Percent(100));
        }
        if (_selected < 0 || _selected >= store.Trips.Count)
        {
            return page;
        }
        return ui.VStack(page, TripSheet(ref ui, store.Trips[_selected])).Grow().Height(Percent(100));
    }

    private Element TripSheet(ref RenderContext ui, Trip trip)
    {
        var theme = ui.Theme;
        DayBuffer days = default;
        for (var day = 0; day < trip.Days.Length && day < 7; day++)
        {
            var captured = day;
            days[day] = ui.Checkbox(DayIds[day], DayLabel(day))
                .Checked(trip.Days[day])
                .OnClick(
                    this,
                    static (view, e) => view.ToggleDay(e.Payload),
                    checked((ulong)((trip.Id << 8) | captured)))
                .Padding(Px(4));
        }
        Span<Element> daySpan = days;

        Span<Element> stars =
        [
            StarButton(ref ui, this, theme, trip, 1),
            StarButton(ref ui, this, theme, trip, 2),
            StarButton(ref ui, this, theme, trip, 3),
            StarButton(ref ui, this, theme, trip, 4),
            StarButton(ref ui, this, theme, trip, 5),
        ];

        var panel = ui.VStack(
                ui.Image(CoverPath)
                    .Fit(ImageFit.Cover)
                    .Width(Percent(100))
                    .Height(Px(150))
                    .Radius(Px(14))
                    .Background(theme.Colors.ElementActive),
                ui.HStack(
                        ui.Text(trip.Title)
                            .FontSize(Px(theme.Typography.Heading))
                            .TextColor(theme.Colors.Text),
                        ui.Spacer(),
                        ui.Button("trip-close", "Close")
                            .OnClick(this, static (view, _) => view.CloseSheet())
                            .Style(WanderStyles.Button(theme))
                    )
                    .ItemsCenter(),
                ui.Text("Daily plan").FontSize(Px(theme.Typography.Detail)).TextColor(theme.Colors.TextMuted),
                ui.VStack(daySpan[..trip.Days.Length]).Gap(Px(2)),
                ui.Text("Rating").FontSize(Px(theme.Typography.Detail)).TextColor(theme.Colors.TextMuted),
                ui.HStack(stars).Gap(Px(4)).ItemsCenter()
            )
            .Gap(Px(10))
            .Padding(Px(18))
            .Width(Percent(100))
            .Background(theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.Border)
            .Radius(Px(20));

        return ui.Sheet("trip-sheet", panel, SheetSide.Bottom)
            .OnDismiss(this, static (view, _) => view.CloseSheet());
    }

    private static string DayLabel(int day) =>
        day switch
        {
            0 => "Mon",
            1 => "Tue",
            2 => "Wed",
            3 => "Thu",
            4 => "Fri",
            5 => "Sat",
            _ => "Sun",
        };

    private static Element StarButton(
        ref RenderContext ui,
        TripsView view,
        GpuiTheme theme,
        Trip trip,
        int stars
    ) =>
        ui.Button(StarIds[stars - 1], stars <= trip.Rating ? "★" : "☆")
            .OnClick(view, static (v, e) => v.SetRating(e.Payload), checked((ulong)((trip.Id << 8) | stars)))
            .Style(WanderStyles.Button(theme, WanderButtonVariant.Chip, stars <= trip.Rating))
            .FontSize(Px(20));

    private Element NewTripDialog(ref RenderContext ui)
    {
        var theme = ui.Theme;
        var panel = ui.VStack(
                ui.Text("New trip")
                    .FontSize(Px(theme.Typography.Heading))
                    .TextColor(theme.Colors.Text),
                ui.Input("new-trip-title", new InputOptions(placeholder: "e.g. Desert nights"))
                    .Style(WanderStyles.Field(theme))
                    .OnChanged(this, static (view, e) => view.SetDraft(e.Value))
                    .OnSubmitted(this, static (view, _) => view.CreateTrip())
                    .Width(Percent(100)),
                ui.Text(_error)
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.Error),
                ui.HStack(
                        ui.Spacer(),
                        ui.Button("new-trip-cancel", "Cancel")
                            .OnClick(this, static (view, _) => view.CancelDialog())
                            .Style(WanderStyles.Button(theme)),
                        ui.Button("new-trip-create", "Create")
                            .OnClick(this, static (view, _) => view.CreateTrip())
                            .Style(WanderStyles.Button(theme, WanderButtonVariant.Primary))
                    )
                    .Gap(Px(8))
                    .ItemsCenter()
            )
            .Gap(Px(10))
            .Padding(Px(20))
            .Width(Px(380))
            .Background(theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.Border)
            .Radius(Px(16));

        return ui.Dialog("new-trip-dialog", panel)
            .OnDismiss(this, static (view, _) => view.CancelDialog());
    }
}
