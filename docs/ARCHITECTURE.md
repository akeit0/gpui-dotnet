# Architecture

GPUI.NET is a semantic bridge between a managed application model and a native GPUI renderer. It
does not expose Rust objects to C# or translate each fluent builder call through FFI. Managed code
writes a compact render arena; Rust validates that arena, retains a decoded snapshot, and owns the
frame-sensitive state.

## Ownership boundary

C# owns:

- application and domain state;
- `GpuiApplication`, window definitions, and managed window handles;
- managed `View` instances, props, lifecycle, and child-slot identity;
- application themes and application-specific style variants;
- event handlers and virtual-row renderer methods;
- dirty render descriptions.

Rust owns:

- `gpui::Application`, native windows, and the event loop;
- render-arena allocation, validation, and retained snapshots;
- semantic component materialization;
- scrolling, list/table viewport state, measurements, and row caches;
- retained Input, Slider, and Dock entities;
- deferred-layer geometry, focus, stacking, and dismissal;
- native title-bar hit testing and platform window commands;
- painting and clean repaints.

The boundary rule is: cross the ABI for state transitions or coarse batches, never for every
builder call, frame, pointer delta, or virtual row.

## Render path

```text
View.Render(ref RenderContext)
        │
        ▼
RenderArena
  nodes       component IDs + UTF-8 data ranges
  operations  typed semantic operations
  children    flat parent/child records
        │
        ▼
managed validation
        │ C ABI
        ▼
native validation
        │
        ▼
ValidatedSnapshot ──► semantic adapters ──► GPUI elements
```

The native host owns the arena memory. A managed render may return `RenderGrowRequired`, after
which Rust grows the arena and retries the render. This is why `Render()` and `[GpuiListItem]`
methods must be deterministic and free of application-side effects.

The native `ManagedView` keeps the last valid snapshot. A clean GPUI repaint materializes or paints
that snapshot without calling managed code. `View.Invalidate()` increments the managed retained
version and sends a coalesced native notification.

## Managed view tree

Each window has one `ManagedSession` and one root `View`. Framework-owned children are resolved by
slot through source-generated factories. A slot retains the same child while its requested type is
unchanged; a keyed slot can replace its child type for routes and tabs.

UI ownership, rather than CLR reachability, defines lifetime. An open window owns its root and a
committed parent slot owns its child. Holding a managed reference does not retain either ownership.
Unmount is terminal; an instance that leaves its window or slot cannot join another tree.

`ViewBase` keeps one-shot identity/lifecycle state separate from mounted runtime state. A stable,
non-pooled `ViewCommandRoute` admits any-thread invalidation and controller commands without
touching UI state. A `MountedViewAttachment` owns the GPUI-thread-only native handle,
resource-key sequence, and lazy event-binding collections. Unmount deactivates the route, removes
and resets the attachment, and returns the attachment to a bounded pool before user cleanup.

`View` and `View<TProps>` are sibling authoring shapes over the shared `ViewBase` runtime. Their
type relationship makes required props a compile-time child declaration constraint.

Child views render into retained fragment arenas. The parent snapshot copies those fragments into
the current root arena. Staged props changes and child invalidation mark the necessary fragment and
its ancestors dirty. Application-wide theme changes invalidate every retained fragment because a
theme is ambient render input rather than child props.

New children are session-owned candidates while a transactional render is retried. Tree
replacement commits the new composition before terminally unmounting the old subtree; abandoned
candidates are also unmounted during reconciliation. Unmount proceeds child-first. See
[VIEW_LIFECYCLE.md](VIEW_LIFECYCLE.md).

## Retained resource path

Scroll, List, Table, Input, Slider, and Dock are declarations plus stable resource identities.
Identity is `(window session, owner View handle, UTF-8 key)`. Rust stores the mutable resource object and
reconfigures it from later snapshots instead of recreating it.

Controllers send small commands through the application UI channel. Optional component families
use the same View route with an extension-neutral envelope and schema-owned payload:

```text
managed controller ──► native resource/extension command ──► retained GPUI resource
```

Resource commands execute on the GPUI thread. Declarative snapshots remain authoritative. List
measurement hints such as `Splice` and `Refresh` are committed with the next compatible snapshot;
a mismatch falls back to a safe reset.
Extension payloads are copied before returning through FFI and may wait for the first matching
resource materialization.

## Virtual datasource path

List and Table rows are not managed child views. GPUI requests item indices from a Rust closure;
Rust aligns cache misses to a configured batch and invokes managed code once for that range:

```text
GPUI item request
      │
      ▼
Rust row-batch cache ── hit ──► retained row snapshot
      │ miss
      ▼
list_render_range(start, count)
      │
      ▼
one arena containing count row roots
```

`ListDataSource.ContentRevision` controls row-snapshot validity independently from the root snapshot
revision. Theme changes also evict row batches because rows contain resolved theme colors. List
viewport and measurement state survive either invalidation.

## Applications, windows, and threading

One `GpuiApplication` maps to one native `gpui::Application`. Every `GpuiWindow` maps to an
independent managed session and native root view. Window IDs are stable 64-bit values and also serve
as render-session IDs.

Window and resource commands may originate from managed threads, but all GPUI mutations occur on
the native event-loop thread. The application exits after its final registered window closes. A
failure is recorded against its managed session; other windows continue until normal shutdown.
See [THREADING.md](THREADING.md) for GPUI entity release, managed callback serialization, async
continuations, and the binding's any-thread ingress contract.

## Themes and styles

`GpuiTheme` is application-scoped ambient input. Managed views resolve semantic tokens while
rendering. The private versioned native theme payload supplies explicit appearance and equivalent
resolved roles to native controls, error surfaces, table chrome, scrollbars, and Dock chrome. After
`gpui_component::init`, the native host projects those roles into the global component and base
themes at startup and before refreshing windows for every theme update. Foundation typography,
spacing, radii, shadows, scrollbar mode, and scrollbar motion retain their defaults until the managed semantic
theme deliberately defines corresponding roles.

The native ABI does not carry product variant names or component style objects. Applications define
variants with `IGpuiElementStyle<TTag>` and flatten them to ordinary semantic operations. Native
hover and active operations are transient paint states, not application variant identifiers.

The managed window root is one native tab group. Button, Checkbox, and Radio delegate focus,
Enter/Space activation, accessibility roles/state, and disabled behavior to `gpui-base`; their
foundation callbacks are translated into the existing semantic click packet. Checkbox and Radio
remain controlled by the next managed snapshot.

## Title bars and menus

Title-bar visuals may be native or managed, but window behavior stays native. Semantic
`WindowControlArea` operations become GPUI drag and caption hit-test regions; pointer motion does
not cross into .NET.

`GpuiMenu[]` is a platform-neutral command tree. The native host installs it as the macOS global
application menu. On Windows and Linux, `GpuiTitleBar.RenderWindow` can render the same definitions
as managed popover menus. Applications can bypass the helper and compose the primitives directly.

## Managed project and package split

- `src/Gpui/` contains the managed API and runtime source files.
- `src/Gpui.Core/` builds those sources as the platform-neutral `GPUI.NET.Core` package.
- `src/Gpui.Native/` defines the native aggregate and RID-specific packages.
- `src/Gpui/` also defines the application-facing `GPUI.NET` meta package and analyzer payload.
- `src/Gpui.Generators/` contains the Roslyn source generator.
- `crates/gpui-dotnet/` builds the native host library.

The package boundary permits a compatible custom native host selected through
`NativeRuntimeOptions.LibraryPath`. Every host must satisfy the same ABI version, API-table size,
schema hash, and required entry points.

Optional native component families use the generic NativeExtension envelope. Their typed managed
schema remains in a separate assembly, with its own extension ID, version, and hash. A custom host
links the selected Rust providers with the base runtime at build time and advertises those schemas
through ABI negotiation. GPUI/Rust objects are never passed between independently built libraries.

## Dependency policy

The `external/gpui-component` submodule pins `gpui-base` and `gpui-component` to an exact revision
of the `akeit0/gpui-component` integration fork. Its gitlink and resolved Zed/GPUI revision form one
validated compatibility tuple recorded in [UPSTREAM_BASELINE.md](UPSTREAM_BASELINE.md).
The foundation crates own reusable native behavior and component skins as components migrate;
GPUI.NET retains its ABI,
semantic decoding, managed callback routing, resource identity, and platform integration.

The managed API does not expose `gpui-base` or GPUI implementation types. Platform-specific
implementation remains in Rust; C# APIs should express durable application semantics rather than
backend details. Direct GPUI remains appropriate for application/window integration, low-level
drawing, and behavior not covered by the foundation.

When GPUI lacks a cross-platform capability, keep the absence explicit instead of emulating a
partial platform contract in managed code. Current examples include runtime window repositioning
and a cross-platform accessibility-tree API.
