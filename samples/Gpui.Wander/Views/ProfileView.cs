using static Gpui.Units;

namespace Gpui;

internal readonly record struct ProfileProps(TravelStore Store);

/// <summary>
/// Profile tab: avatar image, draft-then-save identity inputs, notification toggle,
/// retained goal slider, theme switch, demo reset.
/// </summary>
[GpuiView]
internal sealed partial class ProfileView : View<ProfileProps>
{
    private static readonly string AvatarPath = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "cover.svg"
    );

    private readonly Effect<NoProps> _watch;
    private readonly GpuiApplication _application;
    private InputController _name;
    private InputController _bio;
    private SliderController _goal;
    private string _draftName = string.Empty;
    private string _draftBio = string.Empty;

    public ProfileView(ViewConstruction construction, ProfileProps initialProps)
        : base(construction)
    {
        _application = construction.Application;
        _watch = construction.Effect<NoProps>(WatchStore);
    }

    private void WatchStore(EffectScope scope, NoProps input) =>
        scope.Own(CommittedProps.Store.Subscribe(scope.Bind(this, static view => view.Invalidate())));

    private void SetDraftName(string value) => _draftName = value;

    private void SetDraftBio(string value) => _draftBio = value;

    private void Save()
    {
        CommittedProps.Store.SetProfile(
            _draftName.Length == 0 ? CommittedProps.Store.ProfileName : _draftName,
            _draftBio
        );
        _draftName = string.Empty;
        _draftBio = string.Empty;
        Invalidate();
    }

    private void ToggleNotifications() =>
        CommittedProps.Store.SetNotifications(!CommittedProps.Store.Notifications);

    private void SetGoal(float km) => CommittedProps.Store.SetGoal(km);

    private void SetLight() => _application.SetTheme(WanderThemes.Light);

    private void SetDark() => _application.SetTheme(WanderThemes.Dark);

    private void Reset()
    {
        CommittedProps.Store.Reset();
        // The retained slider keeps its native value across renders; re-seat it
        // after the model jumps. Legal here: events may command accepted resources.
        _goal.SetValue(CommittedProps.Store.GoalKm);
    }

    protected override Element Render(in ProfileProps props, ref RenderContext ui)
    {
        ui.Effect(_watch, default);
        var theme = ui.Theme;
        var store = props.Store;

        var likes = 0;
        foreach (var entry in store.Entries)
        {
            if (entry.Liked)
            {
                likes++;
            }
        }

        return ui.VStack(
                ui.HStack(
                        ui.Image(AvatarPath)
                            .Fit(ImageFit.Cover)
                            .Width(Px(84))
                            .Height(Px(84))
                            .Radius(Px(42))
                            .Background(theme.Colors.ElementActive),
                        ui.VStack(
                                ui.Text(store.ProfileName)
                                    .FontSize(Px(theme.Typography.Title))
                                    .TextColor(theme.Colors.Text),
                                ui.Text(store.ProfileBio)
                                    .FontSize(Px(theme.Typography.Detail))
                                    .TextColor(theme.Colors.TextMuted),
                                ui.Text($"{store.Trips.Count:N0} trips · {likes:N0} liked posts")
                                    .FontSize(Px(theme.Typography.Detail))
                                    .TextColor(theme.Colors.TextAccent)
                            )
                            .Gap(Px(2))
                    )
                    .Gap(Px(14))
                    .ItemsCenter(),
                ui.Input(ref _name, new Utf8InputOptions(placeholder: "Display name"u8))
                    .Style(WanderStyles.Field(theme))
                    .OnChanged(this, static (view, e) => view.SetDraftName(e.Value))
                    .Width(Percent(100)),
                ui.Input(ref _bio, new Utf8InputOptions(placeholder: "Bio"u8))
                    .Style(WanderStyles.Field(theme))
                    .OnChanged(this, static (view, e) => view.SetDraftBio(e.Value))
                    .OnSubmitted(this, static (view, _) => view.Save())
                    .Width(Percent(100)),
                ui.Button("profile-save", "Save profile")
                    .OnClick(this, static (view, _) => view.Save())
                    .Style(WanderStyles.Button(theme, WanderButtonVariant.Primary))
                    .Width(Percent(100)),
                ui.Checkbox("profile-notify", "Trip notifications")
                    .Checked(store.Notifications)
                    .OnClick(this, static (view, _) => view.ToggleNotifications())
                    .Padding(Px(8)),
                ui.VStack(
                        ui.Text($"Season goal: {store.GoalKm:0} km")
                            .FontSize(Px(theme.Typography.Detail))
                            .TextColor(theme.Colors.TextMuted),
                        ui.Slider(ref _goal, new SliderOptions(min: 20, max: 400, step: 10, value: store.GoalKm))
                            .Style(WanderStyles.Goal(theme))
                            .OnChanged(this, static (view, e) => view.SetGoal(e.End))
                            .Width(Percent(100))
                    )
                    .Gap(Px(6)),
                ui.HStack(
                        ui.Radio("profile-light", ui.Text("Light"))
                            .Checked(theme.Appearance == GpuiThemeAppearance.Light)
                            .OnClick(this, static (view, _) => view.SetLight())
                            .Padding(Px(8)),
                        ui.Radio("profile-dark", ui.Text("Dark"))
                            .Checked(theme.Appearance == GpuiThemeAppearance.Dark)
                            .OnClick(this, static (view, _) => view.SetDark())
                            .Padding(Px(8))
                    )
                    .Gap(Px(8)),
                ui.Button("profile-reset", "Reset demo data")
                    .OnClick(this, static (view, _) => view.Reset())
                    .Style(WanderStyles.Button(theme))
                    .Width(Percent(100))
            )
            .Gap(Px(12))
            .Grow();
    }
}
