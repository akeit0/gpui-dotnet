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

    private readonly record struct ProfileInput(
        TravelStore Store,
        ulong ResetRevision,
        string Name,
        string Bio,
        float Goal
    );

    private readonly Effect<ProfileProps> _watch;
    private readonly Effect<ProfileInput> _sync;
    private ProfileInput? _synced;
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
        _watch = construction.Effect<ProfileProps>(WatchStore);
        _sync = construction.Effect<ProfileInput>(SyncInputs);
        _draftName = initialProps.Store.ProfileName;
        _draftBio = initialProps.Store.ProfileBio;
    }

    private void WatchStore(EffectScope scope, ProfileProps input) =>
        scope.Own(input.Store.Subscribe(scope.Bind(this, static view => view.Invalidate())));

    private static ProfileInput Snapshot(TravelStore store) =>
        new(store, store.ResetRevision, store.ProfileName, store.ProfileBio, store.GoalKm);

    private void SyncInputs(EffectScope scope, ProfileInput input) => Synchronize(input);

    private void Synchronize(ProfileInput input)
    {
        var previous = _synced;
        var reset =
            previous is null
            || !ReferenceEquals(previous.Value.Store, input.Store)
            || previous.Value.ResetRevision != input.ResetRevision;
        // External changes win only for the changed field; document resets replace all drafts.
        if (reset || previous!.Value.Name != input.Name)
        {
            _draftName = input.Name;
            _name.SetValue(_draftName);
        }
        if (reset || previous!.Value.Bio != input.Bio)
        {
            _draftBio = input.Bio;
            _bio.SetValue(_draftBio);
        }
        if (reset || previous!.Value.Goal != input.Goal)
            _goal.SetValue(input.Goal);
        _synced = input;
    }

    private void SetDraftName(string value) => _draftName = value;

    private void SetDraftBio(string value) => _draftBio = value;

    private void Save()
    {
        var store = CommittedProps.Store;
        // Store notifications are queued. Reconcile before writing even if their render has
        // not been accepted yet, so Save cannot overwrite a newer external field or reset.
        Synchronize(Snapshot(store));
        store.SetProfile(_draftName, _draftBio);
        // Preserve the accepted draft; another Save must not clear an untouched field.
        _draftName = store.ProfileName;
        _draftBio = store.ProfileBio;
        _name.SetValue(_draftName);
        _bio.SetValue(_draftBio);
        _synced = Snapshot(store);
        Invalidate();
    }

    private void ToggleNotifications() =>
        CommittedProps.Store.SetNotifications(!CommittedProps.Store.Notifications);

    private void SetGoal(float km) => CommittedProps.Store.SetGoal(km);

    private void SetLight() => _application.SetTheme(WanderThemes.Light);

    private void SetDark() => _application.SetTheme(WanderThemes.Dark);

    private void Reset() => CommittedProps.Store.Reset();

    protected override Element Render(in ProfileProps props, ref RenderContext ui)
    {
        ui.Effect(_watch, props);
        ui.Effect(_sync, Snapshot(props.Store));
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
                        ui.Slider(
                                ref _goal,
                                new SliderOptions(min: 20, max: 400, step: 10, value: store.GoalKm)
                            )
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
