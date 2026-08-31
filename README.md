# GPUI.NET

GPUI.NET is an experimental .NET frontend for Rust [GPUI](https://gpui.rs/). C# owns durable application state and publishes compact render IR; Rust owns GPUI, native interaction state, virtualization, event delivery, and frame-sensitive work.

The public API is intentionally semantic rather than a one-to-one mirror of GPUI's Rust API. GPUI can evolve behind native adapters without forcing equivalent churn into every C# application.

Native component behavior and architecture are informed by [GPUI Component](https://github.com/longbridge/gpui-component) and its `gpui-base` foundation. It is a design reference across the component system, while GPUI.NET keeps an independent semantic ABI and its pinned GPUI dependency. See [NOTICE](NOTICE) and [Architecture](docs/ARCHITECTURE.md#design-reference-and-dependency-policy).

## Architecture

```text
C# View state
   │ dirty render only
   ▼
RenderArena (nodes / ops / children / UTF-8)
   │ ABI v5
   ▼
Rust validation + flat retained snapshot
   │
   ├─ semantic NativeAdapter ─────────────► GPUI elements
   │    └─ deferred overlays, tooltips, and pointer context menus
   │
   └─ retained native resources
        ├─ ScrollHandle
        ├─ ListState + managed row batch cache
        └─ ManagedInput + GPUI input handler
```

Clean GPUI repaints do not call managed `Render()`. Scroll wheel/trackpad movement stays native. A virtualized list crosses back into managed code only when Rust needs a row batch that is not already cached.

## Application and windows

One managed application owns one native GPUI event loop and any number of independent window roots:

```csharp
var application = new GpuiApplication();

var main = application.OpenWindow(
    new MainView(),
    new GpuiWindowOptions { Title = "Main", Width = 1040, Height = 700 });

var inspector = application.OpenWindow(
    new InspectorView(),
    new GpuiWindowOptions { Title = "Inspector", Left = 80, Top = 80 });

application.Run(); // returns after the last window closes
```

Each `GpuiWindow` has a stable 64-bit ID and its own root View tree, retained resource store, render snapshots, failures, and mount lifetime. `OpenWindow` also works while the application is running. `Close`, `Activate`, `Minimize`, `ToggleMaximize`, `SetTitle`, and `Resize` enqueue thread-safe native UI commands; title/size changes made before `Run` update the initial window definition. Initial `Left`/`Top` positioning is supported, while runtime repositioning is not exposed by the pinned GPUI API.

For a single window, `GpuiApplication.Run(view, options)` is a convenience over the same model.

Window title bars can be system-provided (the default), custom, or hidden. Custom title bars keep presentation in managed composition while native GPUI hit-test regions perform window dragging and caption actions without routing pointer motion through .NET:

```csharp
var window = application.OpenWindow(
    new DocumentView(),
    new GpuiWindowOptions
    {
        Title = "Document",
        TitleBarStyle = WindowTitleBarStyle.Custom,
    });

var titleBar = ui.HStack(
        ui.Text("Document").Grow(),
        ui.Button("minimize", "—")
            .WindowControlArea(WindowControlArea.Minimize),
        ui.Button("maximize", "□")
            .WindowControlArea(WindowControlArea.Maximize),
        ui.Button("close", "×")
            .WindowControlArea(WindowControlArea.Close))
    .WindowControlArea(WindowControlArea.Drag);
```

Child caption regions override the parent drag region. Leave right-click on the Drag region unclaimed so platform non-client behavior remains available; GPUI.NET does not synthesize a Windows system menu. Caption buttons can use `HoverBackground(Color)` for native neutral/minimize feedback and a destructive red close hover without managed pointer callbacks. On Windows, the sample follows Zed's 36-pixel controls, 10-pixel Segoe caption glyphs, and `#E81120` close hover. On macOS, `Custom` retains the native traffic lights; applications should reserve their space instead of drawing duplicate caption buttons. `Hidden` removes the platform title-bar surface entirely but still allows explicit native control regions.

## Managed Views: framework ownership by default

Normal composition does not require child View fields:

```csharp
[GpuiView]
internal sealed partial class DashboardView : View
{
    protected override Element Render(ref RenderContext ui) =>
        ui.HStack(
            ui.Child<CounterView>("counter"),
            ui.Child<PreferencesView>("preferences"));
}
```

`[GpuiView]` makes the source generator emit an AOT-safe factory. A retained slot keeps the same View instance while the requested type is unchanged. A keyed slot may dynamically replace its View type, which is the normal route/tab pattern:

```csharp
var content = _page switch
{
    Page.Home => ui.Child<HomeView>("content"),
    Page.Settings => ui.Child<SettingsView>("content"),
    _ => throw new InvalidOperationException(),
};
```

When the type changes, the new subtree is rendered and committed before the old subtree is unmounted child-first. Use `View<TProps>` for parent-supplied render inputs instead of constructor model arguments. Explicit `ui.Child(existingInstance, key)` remains an escape hatch when the parent intentionally needs an imperative reference.

## Scroll

A scroll container is declarative while its offset is retained in Rust:

```csharp
return ui.Scroll(
        "document",
        ScrollAxis.Vertical,
        new ScrollOptions(smoothScrolling: true, showScrollbar: true),
        RenderDocument(ref ui))
    .Grow()
    .Height(Percent(100));
```

The resource identity is `(managed View handle, key)`, so different Views may safely use the same local key. Wheel/trackpad updates do not invoke managed code. Smooth wheel scrolling and a draggable overlay scrollbar are enabled by default; pass `new ScrollOptions(false, false)` to disable them.

Create a controller only when imperative access is required:

```csharp
private ScrollController _scroll;

protected override void OnMounted(ref ViewContext context) =>
    _scroll = context.CreateScrollController("document");

[GpuiEvent]
private void ToTop() => _scroll.ScrollToTop();
```

`ScrollController` also supports `ScrollTo(x, y)` and `ScrollToBottom()`.

## Virtualized List

List virtualization is not implemented as thousands of managed child Views. Rust owns `gpui::ListState`, viewport/measurement state, and a small cache of managed row batches. C# supplies only requested rows.

```csharp
[GpuiView]
internal sealed partial class ActivityView : View
{
    private const int Count = 20_000;
    private ListController _list;
    private ulong _rowsRevision;

    [GpuiListItem]
    private Element ActivityRow(int index, ref RenderContext ui) =>
        ui.Button("row", $"Row {index:N0}")
            .OnClick(Events.SelectRow, checked((ulong)index));

    [GpuiEvent]
    private void SelectRow(ClickEvent e)
    {
        var index = checked((int)e.Payload);
        // update model/state...
        _rowsRevision++;
        Invalidate();
    }

    protected override Element Render(ref RenderContext ui) =>
        ui.List(
                ref _list,
                new ListDataSource(Count, _rowsRevision),
                Rows.ActivityRow,
                new ListOptions(
                    batchSize: 48,
                    overdraw: 320,
                    estimatedItemHeight: 40,
                    smoothScrolling: true,
                    showScrollbar: true))
            .Grow()
            .Height(Percent(100));
}
```

`[GpuiListItem]` methods are synchronous, allocation-free dispatch targets with signature:

```csharp
Element Method(int index, ref RenderContext ui)
```

The source generator emits `Rows.Method`, which contains a 64-bit renderer token (`ViewHandle | RendererId`). GPUI may ask its Rust item-render closure for individual indices, but Rust aligns those requests into coarse batches before crossing the ABI. The default batch size is 48 and at most four native row batches are retained per List resource.

`ListDataSource(count, contentRevision)` is the optimized contract: keep the revision stable when a managed render cannot affect row output and increment it whenever cached row content may change. This lets native batches survive unrelated parent renders. The older `itemCount` overload remains conservative and drops batches after each managed snapshot, so adopting explicit revisions is optional and cannot silently make existing code stale.

Lists also use smooth native wheel scrolling and a draggable scrollbar by default. `estimatedItemHeight` keeps the thumb stable and lets dragging jump through an unmeasured virtual list without eagerly rendering every row.

List row renderers are intentionally **Element-only**. They cannot create managed child Views, nested retained `Scroll`, or nested `List` resources. This keeps virtualization lifetime independent from the normal managed View tree. Row events remain supported; `OnClick(callback, payload)` stores an unmanaged `ulong` payload directly in the render IR.

Optional `ListController` operations:

- `ScrollToItem(index)` keeps scrolling native;
- `Splice(start, removed, inserted)` preserves GPUI measurements across a structural model change;
- `Refresh(start, count)` invalidates measurements for a stable range whose content/height changed;
- `Reset(itemCount)` declares a wholesale datasource replacement.

Structural commands are queued as measurement hints and committed together with the next managed snapshot revision. The managed snapshot remains authoritative; a hint/count mismatch falls back to a safe full native reset.

Native `ListState` remains retained across managed renders. Explicit datasource revisions invalidate row batches only when the revision changes; the legacy overload conservatively invalidates after every managed snapshot. Clean scrolling keeps cached batches and crosses into C# only for missing ranges.

## Images

Images use GPUI's native decoder and cache. The managed render arena carries only a UTF-8 filesystem path and presentation options:

```csharp
var image = ui.Image(Path.Combine(AppContext.BaseDirectory, "Assets", "hero.svg"))
    .Fit(ImageFit.Cover)
    .Grayscale(false)
    .Width(Px(320))
    .Height(Px(180))
    .Radius(Px(12));
```

`ImageFit` supports `Fill`, `Contain`, `Cover`, `ScaleDown`, and `None`. Image overflow is clipped to its styled bounds, so `Cover` composes correctly with fixed sizes and rounded corners. A load or decode failure renders a neutral fallback instead of leaving an unexplained blank region.

`ui.Image` currently accepts filesystem paths only; relative paths resolve against the application working directory. URI sources are intentionally not exposed until the native application has a real HTTP client and an explicit network-loading policy.

## Input

`ui.Input` is a retained native single-line editor. GPUI owns focus, selection, clipboard actions, IME composition, caret painting, horizontal scrolling, and password masking. A managed rerender reconfigures presentation and event bindings without replacing the current native value.

```csharp
private InputController _search;

[GpuiEvent]
private void SearchChanged(InputEvent input)
{
    // Decode only when the application actually needs a System.String.
    _model = input.Value;
    Invalidate();
}

protected override Element Render(ref RenderContext ui) =>
    ui.Input(
            ref _search,
            new Utf8InputOptions(placeholder: "Search…"u8))
        .OnChanged(Events.SearchChanged)
        .Width(Percent(100));
```

`InputOptions` is the string convenience API. `Utf8InputOptions`, ref-bound controller IDs, `InputController.SetValue(ReadOnlySpan<byte>)`, and `InputEvent.Utf8Value` avoid unnecessary UTF-16 transcoding. A ref-bound controller receives a stable per-view ID on its first render and retains the UTF-8 key bytes for later renders and commands. Input event bytes are copied once into event-owned memory so async handlers are safe; `InputEvent.Value` decodes lazily. The warmed-up UTF-8 `SetValue` command pins its caller span for the synchronous ABI call and allocates no managed payload array.

Events are opt-in: `OnChanged`, `OnSubmitted`, and `OnFocusChanged`. Without them, native edits do not cross into managed code. `InputController` provides `Focus`, `Blur`, `SelectAll`, and `SetValue`; the initial value is consumed only when the keyed native resource is first created.

On Windows, focused inputs use the active system IME, including composition text and hardware language-mode keys such as Japanese Hankaku/Zenkaku. The native dependency is pinned to the official Zed commit for [PR #41259](https://github.com/zed-industries/zed/pull/41259), which routes IME-owned keys through the standard Win32 message translation flow. Applications do not need to toggle the IME themselves.

## Overlays

`ui.Overlay` paints ordinary semantic content in a deferred, window-relative layer. The default is a centered modal with a dim backdrop, 16-pixel viewport margin, backdrop dismissal, and Escape dismissal:

```csharp
var dialog = ui.VStack(
        ui.Text("Preferences"),
        ui.Input("name"u8),
        ui.Button("close", "Close").OnClick(Events.Close))
    .Padding(Px(20))
    .Width(Px(440))
    .Background(Rgb(255, 255, 255));

return ui.Overlay("preferences"u8, dialog)
    .OnDismiss(Events.Close);
```

`OverlayOptions` supports nine viewport placements, modal/non-modal behavior, priority, margin, backdrop color, and independent backdrop/Escape dismissal. Modal overlays block pointer and wheel input behind the backdrop, move focus into the overlay on open, keep Tab/Shift+Tab traversal inside it, and restore the previous focus when native dismissal runs. Escape is handled in the bubble phase so a focused child control receives it first. Content can contain normal managed child Views and retained controls such as Input; overlays are intentionally unavailable inside virtualized List rows.

`ui.Tooltip(key, trigger, content, options)` adds native hover timing to any semantic trigger and paints ordinary semantic content in a deferred layer. Defaults follow the interaction model used by GPUI Component: a 500 ms show delay, a 300 ms grace period for crossing from trigger to content, automatic side selection, centered alignment, an 8-pixel gap, and an 8-pixel viewport margin.

```csharp
return ui.Tooltip(
    "save-help"u8,
    ui.Button("save", "Save"),
    ui.Text("Writes the current document")
        .Padding(Px(8))
        .Background(Rgb(15, 23, 42))
        .TextColor(Rgb(255, 255, 255)));
```

`TooltipOptions` can request Top/Right/Bottom/Left placement and Start/Center/End alignment; native geometry flips to another side and clamps to the viewport when needed. Show/hide delays can be zero. Moving into the tooltip during its grace period keeps rich content available, while pointer press dismisses it. Tooltip identity is scoped by the owning managed View, so reusable child Views may use local keys safely. Tooltips are intentionally unavailable inside virtualized List rows.

`ui.ContextMenu(key, trigger, content, options)` is the pointer-anchored counterpart. The first child remains in normal layout; right-clicking it opens the second child at the pointer in a deferred layer:

```csharp
var documentMenu = ui.VStack(
        ui.Text("Document actions"),
        ui.Button("open-menu"u8, "Open"u8).OnClick(Events.OpenDocument),
        ui.Button("duplicate-menu"u8, "Duplicate"u8)
            .OnClick(Events.DuplicateDocument))
    .Width(Px(220))
    .Padding(Px(8))
    .Background(Rgb(255, 255, 255));

var documentCard = ui.Text("Right-click this document")
    .Padding(Px(16))
    .Background(Rgb(248, 250, 252));

return ui.ContextMenu("document-actions"u8, documentCard, documentMenu);
```

Rust owns right-button interception, pointer placement, viewport clamping, deferred priority, focus restoration, and dismissal on outside click, Escape, selection, or another right-click. Managed code owns menu visuals and actions through normal generated event bindings. `ContextMenuOptions` configures priority and viewport margin. Context menus are keyed to the owning View and are intentionally unavailable inside virtualized List rows. Use them for application content; leave a custom title-bar Drag region unwrapped so platform non-client behavior remains unclaimed.

`ui.PopoverMenu(key, trigger, content, options)` uses the same two-child ownership model for a left-click trigger. Rust measures the trigger, attaches the deferred menu below its leading edge, snaps it to the viewport, moves focus into it, and dismisses it after selection, outside pointer-down, or Escape. While one menu is open, hovering another `PopoverMenu` in the same View transfers the open menu without another click. This is the primitive used by the sample's File/View/Help title-bar menu:

```csharp
return ui.PopoverMenu(
    "file-menu"u8,
    ui.Button("file-trigger"u8, "File"u8),
    ui.VStack(
        ui.Button("new-window"u8, "New window"u8).OnClick(Events.NewWindow),
        ui.Button("close-window"u8, "Close window"u8).OnClick(Events.CloseWindow)));
```

## Generated events

Constructor-time callback registration is not used. `[GpuiEvent]` emits stable handler IDs, `Events.*` bindings and direct dispatch code. `Task`/`ValueTask` completion is observed; `async void` is rejected.

```csharp
[GpuiEvent]
private async ValueTask Increment(ClickEvent _)
{
    await Task.Delay(100, Lifetime);
    _count++;
    Invalidate();
}
```

`ClickEvent.Payload` is optional unmanaged event context. Normal bindings use zero; virtualized rows can bind an index/model ID without creating a managed closure.

`Render()` remains a pure description function. Arena growth may retry it. Do not mutate View state, start tasks, perform I/O, register handlers, or call `Invalidate()` from `Render()` or a `[GpuiListItem]` renderer.

## ABI v5

ABI v5 makes application ownership distinct from window/render sessions. It adds:

- one native GPUI application with multiple independently mounted managed window roots;
- application-started and per-window-closed lifecycle callbacks;
- thread-safe open, close, activate, minimize, maximize/restore, title, and resize commands;
- stable 64-bit window/session IDs and per-window resource namespaces;
- shutdown only after the final application-owned window closes.

Managed startup validates both ABI version and semantic schema hash (`0x66378CABCEE9092C`). See [docs/ABI.md](docs/ABI.md).

## Build

Prerequisites:

- .NET SDK `10.0.303` (pinned in `global.json`)
- current Rust toolchain with Cargo
- GPUI platform prerequisites

```powershell
dotnet build Gpui.slnx
dotnet run --project samples/Gpui.Sample
```

The sample starts with custom chrome, compact platform-style caption controls, and File/View/Help popover menus in the title bar. It also contains retained Scroll and Input routes, a 20,000-row variable-height virtualized List route, an Image gallery, centered-dialog/right-sheet Overlay demos, delayed trigger-relative tooltips, an application-content ContextMenu demo, and a multi-window gallery. Closing either companion leaves the main gallery running. To open the system-title-bar companion automatically at startup:

```powershell
dotnet run --project samples/Gpui.Sample -- --multi-window
```

To force native-owned render-arena growth/retry:

```powershell
dotnet run --project samples/Gpui.Sample -- --stress-growth
```

## Binding generation

`bindings/schema.json` is the semantic render protocol source of truth and generates:

- `src/Gpui/Rendering/Semantic.g.cs`
- `crates/gpui-src/semantic.g.rs`

```powershell
dotnet run --project tools/Gpui.Bindings.Generator -- generate
dotnet run --project tools/Gpui.Bindings.Generator -- verify
```

`Gpui.Generators` is a separate Roslyn incremental generator for `[GpuiView]`, `[GpuiEvent]`, and `[GpuiListItem]`.

## Packaging

The intended distribution is one managed NuGet containing the source generator and RID-specific native assets:

```text
Gpui.nupkg
  lib/net10.0/Gpui.dll
  analyzers/dotnet/cs/Gpui.Generators.dll
  runtimes/win-x64/native/gpui_dotnet.dll
  runtimes/linux-x64/native/libgpui_dotnet.so
  runtimes/osx-x64/native/libgpui_dotnet.dylib
  runtimes/osx-arm64/native/libgpui_dotnet.dylib
```

See `docs/` for architecture, ABI, component/resource authoring, lifecycle, performance, generation and packaging details.
