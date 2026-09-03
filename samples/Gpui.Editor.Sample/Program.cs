using Gpui;
using Gpui.Editor;
using System.Text;
using static Gpui.Units;

var hostName = OperatingSystem.IsWindows()
    ? "gpui_dotnet_editor.dll"
    : OperatingSystem.IsMacOS()
        ? "libgpui_dotnet_editor.dylib"
        : "libgpui_dotnet_editor.so";
var application = new GpuiApplication(
    new NativeRuntimeOptions
    {
        LibraryPath = Path.Combine(AppContext.BaseDirectory, hostName),
        Extensions = [EditorExtension.Requirement],
    }
);
application.SetTheme(GpuiTheme.CreateDefault(GpuiThemeAppearance.Dark));
application.OpenWindow(
    new EditorSampleView(),
    new GpuiWindowOptions
    {
        Title = "GPUI.NET Optional Editor",
        Width = 1180,
        Height = 760,
    }
);
application.Run();

[GpuiView]
internal sealed partial class EditorSampleView : View
{
    private EditorController _editor;
    private bool _observeChanges = true;
    private bool _lineNumbers = true;
    private bool _fixedLineNumberWidth = true;
    private bool _folding = true;
    private bool _showWhitespace;
    private bool _readOnly;
    private bool _disabled;
    private ulong _baseRevision;
    private ulong _revision;
    private int _changeCount;
    private string _lastChange = "Waiting for a native edit";
    private string _lastInsertion = "Type in the editor to inspect its UTF-8 delta.";
    private string _lastCommand = "No managed command sent.";

    protected override void OnMounted(ref ViewContext context)
    {
        _editor = context.CreateEditorController("main-document");
        _editor.Bootstrap(
            "// This Rope, selection, undo stack, scrolling, and IME live in Rust.\n"
                + "// Edit this document to see revisioned UTF-8 deltas in the sidebar.\n\n"
                + "fn main() {\n"
                + "    let message = \"Hello from the optional GPUI.NET editor host\";\n"
                + "    println!(\"{message}\");\n"
                + "}\n"
        );
    }

    protected override Element Render(ref RenderContext ui)
    {
        var theme = ui.Theme;
        var options = new EditorOptions
        {
            Language = "rust",
            Disabled = _disabled,
            ReadOnly = _readOnly,
            LineNumbers = _lineNumbers,
            LineNumberWidth = _fixedLineNumberWidth ? Px(56) : null,
            Folding = _folding,
            ShowWhitespace = _showWhitespace,
        };
        var editor = _observeChanges
            ? ui.Editor(
                _editor,
                this,
                static (view, changed) => view.OnEditorChanged(changed),
                static (view, rejected) => view.OnEditorCommandRejected(rejected),
                options
            )
            : ui.Editor(_editor, options);

        var header = ui.VStack(
                ui.HStack(
                        ui.VStack(
                                ui.Text("Optional native editor"u8)
                                    .FontSize(Px(theme.Typography.Heading))
                                    .TextColor(theme.Colors.Text),
                                ui.Text(
                                        "A separate C# schema assembly paired with a build-time custom Rust host."u8
                                    )
                                    .FontSize(Px(theme.Typography.BodySmall))
                                    .TextColor(theme.Colors.TextMuted)
                            )
                            .Gap(Px(4)),
                        ui.Spacer(),
                        ui.Badge(ui.Text("schema v5"u8))
                            .Background(theme.Colors.InfoBackground)
                            .TextColor(theme.Colors.Info)
                    )
                    .ItemsCenter(),
                ui.HStack(
                        FeatureBadge(ref ui, "gpui-component Editor"),
                        FeatureBadge(ref ui, "Rust Rope"),
                        FeatureBadge(ref ui, "Native undo + IME"),
                        FeatureBadge(ref ui, "Revisioned UTF-8 events")
                    )
                    .Gap(Px(8))
            )
            .Gap(Px(12));

        var liveStatus = ui.VStack(
                ui.HStack(
                        ui.Text("LIVE PROTOCOL"u8)
                            .FontSize(Px(theme.Typography.Detail))
                            .TextColor(theme.Colors.TextMuted),
                        ui.Spacer(),
                        ui.Badge(ui.Text(_observeChanges ? "BOUND" : "NATIVE ONLY"))
                            .Background(
                                _observeChanges
                                    ? theme.Colors.SuccessBackground
                                    : theme.Colors.WarningBackground
                            )
                            .TextColor(
                                _observeChanges ? theme.Colors.Success : theme.Colors.Warning
                            )
                    )
                    .ItemsCenter(),
                ui.Text($"Revision {_revision:N0}")
                    .FontSize(Px(theme.Typography.Title))
                    .TextColor(theme.Colors.Text),
                ui.Text($"Base {_baseRevision:N0}  ·  {_changeCount:N0} observed transactions")
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextMuted),
                ui.Divider(),
                ui.Text(_lastChange)
                    .FontSize(Px(theme.Typography.BodySmall))
                    .TextColor(theme.Colors.TextAccent)
                    .Width(Percent(100)),
                ui.Text(_lastInsertion)
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextMuted)
                    .Width(Percent(100)),
                ui.Text(_lastCommand)
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.Warning)
                    .Width(Percent(100))
            )
            .Gap(Px(7))
            .Width(Percent(100))
            .Padding(Px(14))
            .Background(theme.Colors.InfoBackground)
            .Radius(Px(10));

        var optionsPanel = ui.VStack(
                ui.Text("EDITOR OPTIONS"u8)
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextMuted),
                Option(
                    ref ui,
                    "observe-events",
                    "Observe change events",
                    _observeChanges,
                    EditorOption.ObserveChanges
                ),
                Option(
                    ref ui,
                    "line-numbers",
                    "Line numbers",
                    _lineNumbers,
                    EditorOption.LineNumbers
                ),
                Option(
                    ref ui,
                    "fixed-line-number-width",
                    "Fixed 56 px number column",
                    _fixedLineNumberWidth,
                    EditorOption.FixedLineNumberWidth
                ),
                Option(
                    ref ui,
                    "folding",
                    "Code folding",
                    _folding,
                    EditorOption.Folding
                ),
                Option(
                    ref ui,
                    "whitespace",
                    "Show whitespace",
                    _showWhitespace,
                    EditorOption.ShowWhitespace
                ),
                Option(
                    ref ui,
                    "read-only",
                    "Read only",
                    _readOnly,
                    EditorOption.ReadOnly
                ),
                Option(
                    ref ui,
                    "disabled",
                    "Disabled",
                    _disabled,
                    EditorOption.Disabled
                )
            )
            .Gap(Px(5));

        var ownership = ui.VStack(
                ui.Text("OWNERSHIP BOUNDARY"u8)
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextMuted),
                ui.Text("• C# owns application state and policy"u8),
                ui.Text("• Rust owns frame-sensitive editing"u8),
                ui.Text("• The document is not resent on render"u8),
                ui.Text("• Events disappear when unbound"u8)
            )
            .Gap(Px(6))
            .FontSize(Px(theme.Typography.Detail))
            .TextColor(theme.Colors.TextMuted);

        var sidebar = ui.VStack(liveStatus, optionsPanel, ui.Divider(), ownership)
            .Gap(Px(16))
            .Padding(Px(16))
            .Width(Px(400))
            .Height(Percent(100))
            .Background(theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.BorderVariant)
            .Radius(Px(12));

        var editorPanel = ui.VStack(
                ui.HStack(
                        ui.Text("main.rs"u8)
                            .FontSize(Px(theme.Typography.BodySmall))
                            .TextColor(theme.Colors.Text),
                        ui.Spacer(),
                        ui.Text(_disabled ? "DISABLED" : _readOnly ? "READ ONLY" : "EDITING")
                            .FontSize(Px(theme.Typography.Detail))
                            .TextColor(
                                _readOnly || _disabled
                                    ? theme.Colors.Warning
                                    : theme.Colors.Success
                            )
                    )
                    .ItemsCenter()
                    .Padding(Px(12))
                    .Background(theme.Colors.TabBarBackground),
                ui.HStack(
                        CommandButton(ref ui, "editor-focus", "Focus", EditorCommand.Focus),
                        CommandButton(
                            ref ui,
                            "editor-caret-start",
                            "Caret at 0",
                            EditorCommand.CaretStart
                        ),
                        CommandButton(
                            ref ui,
                            "editor-insert-marker",
                            "Insert marker",
                            EditorCommand.InsertMarker
                        ),
                        CommandButton(
                            ref ui,
                            "editor-replace",
                            "Replace",
                            EditorCommand.Replace
                        ),
                        CommandButton(
                            ref ui,
                            "editor-stale",
                            "Stale probe",
                            EditorCommand.StaleProbe
                        )
                    )
                    .Gap(Px(8))
                    .Padding(Px(10))
                    .Background(theme.Colors.SurfaceBackground),
                ui.Divider(),
                editor.Grow().Width(Percent(100))
            )
            .Grow()
            .Height(Percent(100))
            .Background(theme.Colors.PanelBackground)
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.BorderVariant)
            .Radius(Px(12));

        return ui.VStack(
                header,
                ui.Divider(),
                ui.HStack(sidebar, editorPanel)
                    .Gap(Px(16))
                    .Grow()
                    .Width(Percent(100))
            )
            .Gap(Px(16))
            .Padding(Px(20))
            .Width(Percent(100))
            .Height(Percent(100))
            .Background(theme.Colors.Background)
            .TextColor(theme.Colors.Text);
    }

    private void OnEditorChanged(EditorChangedEvent changed)
    {
        _baseRevision = changed.BaseRevision;
        _revision = changed.Revision;
        _changeCount++;

        var edit = changed.Edits[0];
        _lastChange =
            $"{changed.Origin} · byte {edit.Start:N0} · -{edit.DeletedLength:N0} +{edit.InsertedUtf8.Length:N0}";
        var inserted = Encoding.UTF8.GetString(edit.InsertedUtf8.Span)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
        if (inserted.Length > 36)
        {
            inserted = inserted[..36] + "…";
        }
        _lastInsertion = inserted.Length == 0 ? "Inserted UTF-8: ∅" : $"Inserted UTF-8: “{inserted}”";
        Invalidate();
    }

    private void OnEditorCommandRejected(EditorCommandRejectedEvent rejected)
    {
        _revision = rejected.CurrentRevision;
        _lastCommand =
            $"Rejected {rejected.Command}: {rejected.Reason} "
            + $"(expected {rejected.ExpectedRevision:N0}, current {rejected.CurrentRevision:N0})";
        Invalidate();
    }

    private Element Option(
        ref RenderContext ui,
        ReadOnlySpan<char> id,
        ReadOnlySpan<char> label,
        bool value,
        EditorOption option
    ) =>
        ui.Checkbox(id, label)
            .Checked(value)
            .OnClick(
                this,
                static (view, click) => view.ToggleOption((EditorOption)click.Payload),
                (ulong)option
            )
            .Padding(Px(7))
            .Radius(Px(6))
            .HoverBackground(ui.Theme.Colors.ElementHover);

    private static Element FeatureBadge(ref RenderContext ui, ReadOnlySpan<char> label) =>
        ui.Badge(ui.Text(label))
            .Background(ui.Theme.Colors.ElementSelected)
            .TextColor(ui.Theme.Colors.TextAccent);

    private Element CommandButton(
        ref RenderContext ui,
        ReadOnlySpan<char> id,
        ReadOnlySpan<char> label,
        EditorCommand command
    ) =>
        ui.Button(id, label)
            .OnClick(
                this,
                static (view, click) => view.RunCommand((EditorCommand)click.Payload),
                (ulong)command
            )
            .Padding(Px(7));

    private void RunCommand(EditorCommand command)
    {
        switch (command)
        {
            case EditorCommand.Focus:
                _editor.Focus();
                _lastCommand = "Sent Focus (revision independent).";
                break;
            case EditorCommand.CaretStart:
                _editor.SetSelection(_revision, 0, 0);
                _lastCommand = $"Sent SetSelection at revision {_revision:N0}.";
                break;
            case EditorCommand.InsertMarker:
                _editor.ApplyEdit(
                    _revision,
                    new EditorEdit(0, 0, "// inserted through EditorController\n"u8.ToArray())
                );
                _lastCommand = $"Sent ApplyEdit at revision {_revision:N0}.";
                break;
            case EditorCommand.Replace:
                _editor.ReplaceDocument(
                    _revision,
                    "// replaced through a revision-checked extension command\nfn main() {}\n"
                );
                _lastCommand = $"Sent ReplaceDocument at revision {_revision:N0}.";
                break;
            case EditorCommand.StaleProbe:
                _editor.SetSelection(_revision + 1, 0, 0);
                _lastCommand = $"Sent deliberately stale command from revision {_revision + 1:N0}.";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }
        Invalidate();
    }

    private void ToggleOption(EditorOption option)
    {
        switch (option)
        {
            case EditorOption.ObserveChanges:
                _observeChanges = !_observeChanges;
                break;
            case EditorOption.LineNumbers:
                _lineNumbers = !_lineNumbers;
                break;
            case EditorOption.FixedLineNumberWidth:
                _fixedLineNumberWidth = !_fixedLineNumberWidth;
                break;
            case EditorOption.Folding:
                _folding = !_folding;
                break;
            case EditorOption.ShowWhitespace:
                _showWhitespace = !_showWhitespace;
                break;
            case EditorOption.ReadOnly:
                _readOnly = !_readOnly;
                break;
            case EditorOption.Disabled:
                _disabled = !_disabled;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(option));
        }
        Invalidate();
    }

    private enum EditorOption : ulong
    {
        ObserveChanges = 1,
        LineNumbers,
        FixedLineNumberWidth,
        Folding,
        ShowWhitespace,
        ReadOnly,
        Disabled,
    }

    private enum EditorCommand : ulong
    {
        Focus = 1,
        CaretStart,
        InsertMarker,
        Replace,
        StaleProbe,
    }
}
