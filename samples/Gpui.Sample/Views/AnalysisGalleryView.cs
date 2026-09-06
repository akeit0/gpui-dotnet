using Gpui;
using static Gpui.Units;

[GpuiView]
internal sealed partial class AnalysisGalleryView : View
{
    private readonly AnalysisDocument _document = new();
    private readonly GpuiApplication _application;
    private bool _show = true;

    public AnalysisGalleryView(ViewConstruction construction)
        : base(construction) => _application = construction.Application;

    protected override Element Render(ref RenderContext ui) =>
        ui.VStack(
                ui.HStack(
                        ui.Button("change-document", "Change document")
                            .OnClick(this, static (view, _) => view._document.Replace())
                            .Style(SampleStyles.Button(ui.Theme)),
                        ui.Button("toggle-analysis", _show ? "Remove analysis" : "Show analysis")
                            .OnClick(
                                this,
                                static (view, _) =>
                                {
                                    view._show = !view._show;
                                    view.Invalidate();
                                }
                            )
                            .Style(SampleStyles.Button(ui.Theme)),
                        ui.Button("analysis-window", "Open in a window")
                            .OnClick(
                                this,
                                static (view, _) =>
                                    view._application.OpenWindow(
                                        DocumentAnalysisView.Spec(new(view._document, "GPUI")),
                                        new GpuiWindowOptions
                                        {
                                            Title = "Document analysis",
                                            Width = 800,
                                            Height = 520,
                                        }
                                    )
                            )
                            .Style(SampleStyles.Button(ui.Theme))
                    )
                    .Gap(Px(8)),
                _show
                    ? ui.Child("analysis", DocumentAnalysisView.Spec(new(_document, "GPUI")))
                    : ui.Text(
                        "The document survives removal. Showing analysis creates a new local query."
                    )
            )
            .Gap(Px(16))
            .Grow();
}

internal sealed class AnalysisDocument
{
    private int _revision;
    internal string Text { get; private set; } =
        "GPUI owns native interaction. C# owns application state.";
    private event Action? Changed;

    internal void Replace()
    {
        _revision++;
        Text =
            $"Revision {_revision}. GPUI renders native elements. GPUI keeps scrolling and focus native.";
        Changed?.Invoke();
    }

    internal IDisposable Subscribe(Action callback)
    {
        Changed += callback;
        return new Subscription(this, callback);
    }

    private sealed class Subscription(AnalysisDocument document, Action callback) : IDisposable
    {
        public void Dispose() => document.Changed -= callback;
    }
}

internal sealed record DocumentAnalysisProps(AnalysisDocument Document, string InitialQuery);

internal readonly record struct AnalysisInput(string Text, string Query);

[GpuiView]
internal sealed partial class DocumentAnalysisView : View<DocumentAnalysisProps>
{
    private readonly Signal<string> _query;
    private readonly Signal<string> _background = new("Choose Analyze to run background work.");
    private readonly Memo<AnalysisInput, int> _matches;
    private readonly Effect<DocumentAnalysisProps> _subscription;
    private readonly WorkScope _work;

    public DocumentAnalysisView(ViewConstruction construction, DocumentAnalysisProps initialProps)
        : base(construction)
    {
        _query = new(initialProps.InitialQuery);
        _matches = construction.Memo<AnalysisInput, int>();
        _work = construction.Work;
        _subscription = construction.Effect<DocumentAnalysisProps>(Subscribe);
    }

    private void Subscribe(EffectScope scope, DocumentAnalysisProps input) =>
        scope.Own(input.Document.Subscribe(scope.Bind(this, static view => view.Invalidate())));

    private void Analyze()
    {
        var request = new AnalysisInput(CommittedProps.Document.Text, _query.Value);
        _background.Value = "Analyzing…";
        _work.StartLatest(
            this,
            request,
            static (input, lifetime) => Task.Run(() => CountMatches(input), lifetime),
            static (view, count) => view._background.Value = $"Background result: {count} matches"
        );
    }

    private static int CountMatches(AnalysisInput input)
    {
        if (input.Query.Length == 0)
            return 0;
        var count = 0;
        var rest = input.Text.AsSpan();
        int index;
        while ((index = rest.IndexOf(input.Query, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            rest = rest[(index + input.Query.Length)..];
        }
        return count;
    }

    protected override Element Render(in DocumentAnalysisProps props, ref RenderContext ui)
    {
        ui.Effect(_subscription, props);
        var matches = _matches.Get(new(props.Document.Text, _query.Value), CountMatches);
        return ui.VStack(
                ui.Text("Document analysis").FontSize(Px(ui.Theme.Typography.Title)),
                ui.Text(props.Document.Text),
                ui.Input(
                        "query",
                        new InputOptions(initialValue: _query.Value, placeholder: "Search text")
                    )
                    .OnChanged(this, static (view, input) => view._query.Value = input.Value),
                ui.Text($"Matches: {matches}"),
                ui.Button("analyze", "Analyze latest request")
                    .OnClick(this, static (view, _) => view.Analyze())
                    .Style(SampleStyles.Button(ui.Theme)),
                ui.Text(_background.Value)
            )
            .Gap(Px(12))
            .Padding(Px(20))
            .Grow()
            .Background(ui.Theme.Colors.SurfaceBackground)
            .TextColor(ui.Theme.Colors.Text);
    }
}
