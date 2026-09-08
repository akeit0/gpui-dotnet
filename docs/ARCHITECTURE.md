# Architecture

Cross-layer acceptance, displayed-row ownership, and callback-admission invariants are specified
in [Runtime boundaries](RUNTIME_BOUNDARIES.md). Allocation measurements are reported separately in
[Performance](PERFORMANCE.md); architectural guarantees do not imply measured end-to-end costs.

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
- native validation and owned retained snapshots;
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

Managed code owns reusable root, retained-fragment, and range-output arenas. Buffers grow before
writes without rerunning user rendering. Rust receives a borrowed completed descriptor and
synchronously decodes it into an owned snapshot before any further managed callback. Native row
caches retain decoded snapshots, not the borrowed buffers. `Render()` and `[GpuiListItem]` remain
free of observable application-side effects while allowing pure owned caches; this requirement is independent of capacity.
Elements and render contexts retain the managed arena owner. Authoring validates its thread,
disposal state, and captured generation before accessing native memory. Each write keeps the owner
alive until pointer use ends; disposal and reset cannot interrupt an active write or formatter.
Disposal still releases buffers immediately, even when an escaped Element retains the disposed owner.
Row engines reuse numeric validation/grouping scratch across serial batch decodes. A batch keeps
only its decoded snapshot, artifact lease, and cache metadata. Its temporary string interner ends
after decoding; the snapshot owns its strings independently. Root snapshots retain their interner
across renders so consecutive values can reuse allocations.
Reactive invalidation sorts the owned ingress message by source and artifact once. Each row
engine searches its source range, skipping its cache entirely when that range is empty; no
additional artifact registry or persistent index is retained.
Child fragment boundaries check arena identity, generation, and root index before copying. Full
managed semantic validation runs once on the assembled root or row batch before publication;
native decoding independently validates the complete snapshot before acceptance.
Decoded snapshots lazily own a drawing geometry cache. Each Drawing retains at most one bounds
variant after repeated use, with a shared limit of 256 entries and 16 MiB of path/vector capacity
per snapshot. New descriptions detach the old cache after validation; frame-owned handles may keep
it alive until the old frame is released. Cache entries hold geometry and resolved colors only, with
no View callbacks or borrowed arena memory. Snapshots without materialized Drawings allocate no
drawing cache. This is native derived data and does not add a retained resource or managed row View.
Drawing canvases copy commands into one owned buffer per Drawing. A lazy snapshot-owned pool
recycles buffers after prepaint consumes their commands or the canvas is dropped, retaining at
most 256 free buffers and 4 MiB of free command capacity. Live captures exclusively own their
buffers, so decoding a replacement cannot overwrite an earlier frame's commands. Free buffers
can span accepted replacements; a replacement without Drawings detaches the pool. Snapshot and
surviving canvas handles own its lifetime. This scratch budget is separate from geometry retention
and excludes live canvas buffers.
Each root publication returns a non-reused revision. After decoding and resource reconciliation,
Rust acknowledges it through `render_completed`. Managed props and composition commit throughout
the tree, replaced ownership retires, all new routes activate, and effects start parent-first before native materialization.
Normal external callbacks cannot enter between publication and acceptance.

Acceptance follows staged compositions and their immediate child declarations. A reused clean
child accepts any newly supplied equal props, then keeps its descendants' committed state without
walking them. Comparing each rendered parent's previous and staged slots identifies removed
subtree roots for child-first retirement. Exclusive slot ownership makes a full-tree reachability
set unnecessary. Session failure and shutdown still enumerate every attached owner, including
unaccepted construction candidates.

The native `ManagedView` keeps the last valid snapshot. A clean GPUI repaint materializes or paints
that snapshot without calling managed code. `View.Invalidate()` queues a coalesced request using
stable View identity. The application thread consumes it before rendering, marks the View and its
ancestors dirty, and rerenders the required fragments. Propagation stops at an already-dirty
ancestor. Retained tree state uses the application execution guard and needs no locks.

## Managed view tree

Each window has one `ManagedSession` and one root `View` or `View<TProps>`. Roots and children consume
typed Spec declarations and construct through the same generated factory under a pre-existing ownership
scope on the application thread. Framework-owned children are resolved by slot. A slot retains the same child while its requested type is
unchanged; a keyed slot can replace its child type for routes and tabs.

UI ownership, rather than CLR reachability, defines lifetime. An open window owns its root and a
committed parent slot owns its child. Holding a managed reference does not retain either ownership.
Unmount is terminal; an instance that leaves its window or slot cannot join another tree.

`ViewBase` is the authoring boundary. Its composed `ViewRuntime` coordinates one-shot identity,
lifetime, and mounting. A stable, non-pooled `ViewCommandRoute` admits any-thread commands.
`ViewOwnership` owns construction cleanup, memos, and effect handles. `ViewRuntime` owns the optional
View work handle. `MountedViewAttachment` owns the UI handle, resource-key sequence, and
`ViewEventRegistry`; the registry owns event tokens and artifact leases. Unmount deactivates the
route, retires optional capabilities, and resets pooled attachment storage before user cleanup.
See [Managed View runtime](VIEW_RUNTIME.md) for responsibility and lifetime boundaries.

`View` and `View<TProps>` are sibling authoring shapes over the shared `ViewBase` authoring contract. Their
type relationship makes required props a compile-time child declaration constraint.

Child views render into retained fragment arenas. The parent snapshot copies those fragments into
the current root arena. Staged props changes and child invalidation mark the necessary fragment and
its ancestors dirty. Application-wide theme changes invalidate every retained fragment because a
theme is ambient render input rather than child props.

Completing a managed fragment only stages it. Dirty flags clear for reachable staged compositions
when native accepts the root, before effect setup runs. Rejection faults the session and retires its Views. Requests
queued during rendering or pending acceptance are consumed by a later render, so accepting the
current snapshot cannot erase a newer invalidation.

New children construct owned local state and remain session-owned candidates until native acceptance. Tree replacement commits the new composition before terminally unmounting the old
subtree; abandoned candidates release local ownership without starting effects. Unmount proceeds child-first. See
[VIEW_LIFECYCLE.md](VIEW_LIFECYCLE.md).

## Reactive state

`Signal<T>` tracks reads against the current View or demand artifact. Reusable edges become
subscriptions only when native accepts that consumer's output. Changed values dirty only their
accepted consumers; conditional reads remove obsolete edges at acceptance. Change revisions
close the gap between observation and acceptance. Bound reads and writes assert the application's
thread and identity. Teardown detaches edges before user cleanup. See [Reactivity](REACTIVITY.md).

## Retained resource path

Demand rendering is a shared artifact lifecycle, not a List ownership model. Managed request
adapters validate and render an element tree under the session's demand scope. That scope supplies
theme, render-purity guards, dependency tracking, and artifact-owned event bindings. Native
`demand::load_artifact` decodes borrowed output, validates the adapter's expected shape, accepts
the artifact, and returns an owned snapshot and release lease. Source IDs come from the shared
demand module. Neither the common loader nor the managed demand scope assumes rows or indices.

List/Table currently provide the production request adapter: a bounded contiguous range rendered
under a synthetic root, with one child per requested row. Their cache eviction and measurement
policies remain in the row engine. Non-range snapshot tests exercise the same lifecycle. Public
custom demand-renderer registration and a general request ABI are not exposed yet; the existing
`list_render_range` callback remains this adapter's wire entry point.

Scroll, List, Table, Input, Slider, Dock, and custom focus targets are declarations plus stable resource identities.
Identity is `(window session, owner View handle, UTF-8 key)`. Rust stores the mutable resource object and
reconfigures it from later snapshots instead of recreating it.

Controllers send small commands through the application UI channel. Optional component families
use the same View route with an extension-neutral envelope and schema-owned payload:

```text
managed controller ──► native resource/extension command ──► retained GPUI resource
```

Resource commands require an accepted declaration and execute on the GPUI thread. Native ingress
stamps commands with the current presence generation; delivery discards them after that generation
ends. A later declaration under the same key creates a new generation. Declarative snapshots remain authoritative. List
measurement hints such as `Splice` and `Refresh` are committed with the next compatible snapshot;
a mismatch falls back to a safe reset.
Extension payloads are copied before returning through FFI and may wait for the first matching
resource materialization within that generation. Presence is published before effect setup, so
accepted effect setup can command its resources before they materialize.

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
list_render_range(source, start, count) → artifact
      │
      ▼
one arena containing count row roots
```

`ListDataSource.ContentRevision` controls row-snapshot validity independently from the root snapshot
revision. Theme changes also evict row batches because rows contain resolved theme colors. List
viewport and measurement state survive either invalidation.
An optional `ListDataSource.ProjectionRevision` declares replacement of positional identity.
Changing it resets native cursor, viewport, measurements, and batches at reconciliation, even for
equal counts. It overrides queued positional hints; stable stamps preserve the existing splice
contract. C# owns this coarse stamp and model selection; Rust does not scan managed IDs per frame.

Every native row engine owns a non-reused source identity, separate from its generated renderer
method. Every loaded batch owns a managed artifact lease that keeps only that batch's event
bindings live. Eviction, revision/theme invalidation, source removal, and shutdown release those
bindings explicitly. Native decode failure releases the unpublished batch's lease and faults the
session. Artifact release runs no application code, including during root reconciliation.

After range decode, `accept_artifact` commits its reactive observations. Row-only Signal changes
batch source/artifact keys at the managed callback boundary; native evicts and remeasures those
batches without requiring managed root rendering. These identities never reach application code.

Dynamic event tokens identify a never-reused ID under a one-shot View handle. Live IDs map to
recyclable slots. Root rendering retires only root bindings; each artifact releases only its own
bindings. Stale tokens are harmless and released slots no longer retain targets or delegates.

Virtual-row context menus and tooltips use collection-level requests with stable item IDs. The owning
managed View declares the content in its root/fragment snapshot, outside the row batch. A window-owned
native anchor records the pointer position or marked element bounds and artifact identity without
retaining the batch. Tooltip targets are scalar row markers; the collection declares the complete
tooltip options and native timing requests managed content only after sustained hover. Deferred
prepaint checks current geometry and cache identity before exposing content
or hitboxes. Anchor loss expires the request; a stale managed declaration cannot reopen it.
See [Deferred layers](COMPONENTS.md#deferred-layers).

## Applications, windows, and threading

Shortcut bindings are native descriptions attached to Div/Overlay scopes in the focus ancestry.
Native bubbling resolves descendant precedence, exact modifiers, isolation, and consumption before
emitting a matched command. A window-local dispatch marker prevents isolated descendants from
activating ancestor shortcuts without swallowing unrelated native key handling. The root clears
that marker at the start of each key dispatch; callbacks retain ordinary managed View binding lifetime.
Modal overlays isolate page shortcuts. Text-producing shortcuts require a command modifier, and
the router yields to platform character-input events, without per-control protection hooks.
No focus handles or per-key matching decisions cross the ABI.

Custom Div focus targets reuse the retained resource lifecycle. An opt-in key operation binds one
native FocusHandle to the container itself; no wrapper or managed focus-state mirror is created.
The accepted snapshot controls presence and Tab participation, and queued Focus/Blur commands apply
during materialization. Removal retires the handle and pending commands. GPUI owns pointer focus,
Tab traversal, descendant precedence, and keyboard focus paint.

One `GpuiApplication` maps to one native `gpui::Application`. Every `GpuiWindow` maps to an
independent managed session and native root view. Window IDs are stable 64-bit values and also serve
as render-session IDs.

Window and resource commands may originate from managed threads, but all GPUI mutations occur on
the native event-loop thread. The application exits after its final registered window closes. A
failure is recorded against its managed session; other windows continue until normal shutdown.
See [THREADING.md](THREADING.md) for GPUI entity release, managed callback serialization,
owned-work completion, and the binding's any-thread ingress contract.

## Themes and styles

`GpuiTheme` is application-scoped ambient input. Managed views resolve semantic tokens while
rendering. The private versioned native theme payload supplies explicit appearance and equivalent
resolved roles to native controls, error surfaces, table chrome, scrollbars, and Dock chrome. After
`gpui-base` initialization, the native host projects those roles into the global foundation
theme at startup and before refreshing windows for every theme update. Foundation typography,
spacing, radii, shadows, scrollbar mode, and scrollbar motion retain their defaults until the managed semantic
theme deliberately defines corresponding roles.

The native ABI does not carry product variant names or component style objects. Applications define
variants with `IGpuiElementStyle<TTag>` and flatten them to ordinary semantic operations. Native
hover and active operations are transient paint states, not application variant identifiers.

`SurfaceColors` pairs a background and inherited foreground; `InteractionColors` groups complete
normal/hover/pressed pairs. Their `Surface` and `Paint` compositions write existing operations,
without new ABI records or native state. GPUI remains unmodified and provides one inherited
foreground. Secondary child colors stay explicit in application recipes. See
[Styling](STYLING.md) and the [upstream content-color proposal](proposals/GPUI_CONTENT_COLORS.md).

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

The `external/gpui-kit` submodule pins `gpui-base` and `gpui-component` to an exact revision
of the `akeit0/gpui-kit` integration fork. Its gitlink and resolved Zed/GPUI revision form one
validated compatibility tuple recorded in [UPSTREAM_BASELINE.md](UPSTREAM_BASELINE.md).
The default native host links `gpui-base` only; the full `gpui-component` facade is reserved for
custom hosts that select it.
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
