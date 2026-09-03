# gpui-base migration

This is the living development plan and status tracker for adopting `gpui-base` and selected
`gpui-component` facilities as GPUI.NET's native behavior foundation. Keep it aligned with the
current implementation. Exact dependency revisions and the downstream patch ledger live in
[UPSTREAM_BASELINE.md](UPSTREAM_BASELINE.md).

## Objective

Reuse foundation implementations for native interaction, focus, accessibility, controlled state,
text editing, docking, popups, scrolling, motion, and other broadly useful behavior while keeping
the managed/native boundary efficient. GPUI.NET APIs may evolve when foundation concepts produce a
better component model, but adoption should not require disproportionate protocol complexity,
crossing cost, or duplicated machinery merely to replace a working implementation. Rust foundation
types remain private implementation details selected by native adapters.

The target dependency direction is:

```text
C# application and managed Views
        │
        ▼
GPUI.NET semantic IR and native protocol
        │
        ▼
GPUI.NET native adapters and retained identity
        │
        ├── gpui-base reusable behavior, retained components, and the local Dock skin
        ├── selected gpui-component components in custom hosts only (for example the editor host)
        ├── direct GPUI primitives
        └── platform-specific integration
```

The Rust crates are implementation foundations, not part of the managed public contract. C# APIs
do not expose Rust implementation types or inherit the Rust component hierarchy.

## Current status

Status values are `Complete`, `In progress`, `Planned`, and `Decision pending`.

| Phase | Status | Current state | Exit condition |
|---|---|---|---|
| 0. Forked native baseline | Complete | The fork is pinned to an exact SHA, the compatible GPUI revision is locked, CI/builds use `--locked`, and the graph guard resolves one GPUI package. | The dependency tuple is deterministic and documented. |
| 1. Initialization and theme bridge | Complete | `gpui-base` initialization runs before queued window creation. The version 2 native theme payload carries explicit appearance, and startup plus every theme update project managed semantic roles into the foundation theme. | Existing and foundation-backed controls use the same managed-authoritative theme source. |
| 2. Button, Checkbox, and Radio probe | Complete | Root and virtual-row adapters use foundation primitives with stable identity, derived accessible names, controlled checked state, disabled behavior, native focus traversal, and existing click dispatch. | The family is foundation-backed with pointer, keyboard, focus, accessibility, disabled, and controlled-state parity. |
| 3. Protocol checkpoint | Complete | The snapshot and click protocols remain; the schema adds a narrow `disableable` capability and Boolean `Disabled` operation. | Findings from the first control family produce an explicit keep/change decision with tests and updated ABI documentation. |
| 4. Deferred layers | Complete | Tooltip and PopoverMenu use foundation Popup/Positioner, ContextMenu uses Positioner, and PopoverMenu plus ContextMenu use PopoverState. Modal Overlay uses foundation FocusTrapElement, which also covers the managed Dialog and Sheet compositions. GPUI.NET retains its distinct timing, placement, backdrop, priority, topmost, and managed callback semantics. | Foundation behavior replaces duplicated placement, focus, and dismissal infrastructure where semantics match. |
| 5. Slider | Complete | GPUI.NET retains its native Slider engine because the foundation state cannot reconcile configuration and lacks keyboard/focus plus released-event parity. The retained root now exposes foundation-equivalent slider accessibility metadata. | Foundation behavior reaches pointer/keyboard/controller/event parity, or the custom implementation is retained with rationale. |
| 6. Input | Complete | GPUI.NET retains its single-line editing engine because it preserves the revisioned contiguous UTF-8 event path without per-event native value materialization. The retained root now exposes the foundation-equivalent text-input role. | IME, Unicode, selection, clipboard, focus, commands, revisions, and events reach parity before old behavior is removed. |
| 7. Scrolling and scrollbar | Complete | Scroll, List, and Table use foundation Scrollbar interaction and paint. GPUI.NET retains wheel smoothing, controller commands, and gutter geometry while seeding GPUI's native ListState with estimated heights for unmeasured rows. | Foundation scrollbar behavior is adopted selectively without additional managed/native traffic. |
| 8. List and Table evaluation | Complete | GPUI.NET retains GPUI `ListState`, managed aligned range batches, stable row identity, structural commands, and table column reconciliation. Foundation scrollbar behavior remains shared. | Migration to foundation `VirtualList` is rejected because its integration cost and ownership tradeoffs do not provide a corresponding API or performance benefit. |
| 9. Advanced retained components | In progress | DockArea retains a foundation Dock wearing a small in-repo skin (`crates/gpui-dotnet/src/dock_skin.rs`) with stable string panel IDs, declarative center tabs/splits and left/bottom/right regions, ordinary element or child-View content, tab activation, close/zoom controls, dock collapse affordances, resize handles, and native drag/drop interaction. The default host links `gpui-base` only: no `gpui-component` skin crate and no bundled asset provider. The optional Editor probe is split into an independent managed schema and build-time custom host; it now proves one-shot bootstrap, revisioned UTF-8 deltas, typed commands, and explicit stale-command rejection without adding editor contracts to Core, with provider-owned component initialization and theme projection running through the extension lifecycle seam. A reproducible size report, a default-host link-map check, and broad behavior tests remain open. | Advanced components prove stable managed identity, coarse events, native high-frequency interaction, lifecycle, theme integration, optional packaging where appropriate, and no unrelated component families in the default native host. |
| 10. Cleanup and protocol freeze candidate | Planned | Compatibility remains preview-level. | Superseded behavior and protocol paths are removed and the new contract is deliberately reviewed for stability. |

No semantic component is considered migrated merely because the dependency and initializer exist.
Implementation ownership changes only after the component-specific exit criteria pass.

## Foundation theme bridge

`GpuiTheme` remains the application-wide source of truth. The native adapter maps resolved
`NativeTheme` roles into `gpui_base::Theme`; foundation types do not
cross the managed API or ABI.

The private version 2 theme payload carries explicit `Light` or `Dark` appearance rather than
inferring it from colors. The projection supplies foundation color tokens, Dock chrome, scrollbar
paint, resizable-handle colors, and selection color. Foundation typography, radius, spacing, shadow,
scrollbar mode, and scrollbar motion remain at their defaults because GPUI.NET does not yet expose
equivalent semantic roles.

Startup applies the default projection immediately after foundation initialization. Every
`SetTheme` command replaces the projection before windows refresh, without rebuilding retained
resources or component identity. Native tests cover payload validation, appearance, representative
roles, preservation of foundation-owned token groups, and construction of a foundation Button from
the projected tokens.

## First behavior probe

Button, Checkbox, and Radio use `gpui-base` primitives for native activation, Enter/Space keyboard
behavior, focus, accessibility, and disabled state. The managed API remains semantic: `Checked`
supplies controlled Checkbox/Radio state, `Disabled` is available only on the disableable control
family, and `OnClick` remains the callback surface.

The native adapter derives accessible names from descendant Text nodes, translates foundation
change requests into the existing click callback packet, and keeps the next managed snapshot
authoritative. The same adapter behavior is used inside detached virtual-row batches, with identity
derived from the list, model or row position, and semantic node.

The sample exposes a focused control row for pointer, keyboard, disabled, controlled-state,
accessibility, and theme-transition verification.

## Protocol checkpoint decision

The first behavior probe keeps the flat semantic snapshot, managed click callback packet, and
owner/key identity model. It does not justify typed foundation nodes, a general component-state
registry, a new event envelope, new retained commands, or an API-table change.

One schema change is justified: a `disableable` capability scopes the Boolean `Disabled` operation
to Button, Checkbox, and Radio. This changes the generated schema hash but not ABI version 1 or C
record layouts. Foundation Checkbox/Radio change requests map to the existing click callback because
the managed handler already owns the state transition and publishes the authoritative snapshot.

## Deferred-layer decision

Tooltip and PopoverMenu route through `gpui_base::Popup`. A generic
foundation extension makes side placement, alignment, offset, viewport margin, and deferred
priority configurable while preserving the existing Popup defaults. GPUI.NET maps its validated
tooltip placement/alignment values at the native adapter and keeps its public options unchanged.
Popover menus prefer bottom/start placement and now use the shared opposite-side fallback before
viewport clamping. Foundation `PopoverState` owns their open/focus/restoration lifecycle, while
the existing OverlayStack guards outside, selection, and Escape dismissal so only the topmost
capturing layer can close.

Pointer-anchored ContextMenu uses `gpui_base::Positioner::corner` for placement and viewport
clamping, and Foundation `PopoverState` owns its open/focus/restoration lifecycle. GPUI.NET retains
the full-window backdrop and OverlayStack guards so outside, selection, right-click, and Escape
dismissal preserve their existing ordering and input interception.

Modal Overlay registers its focus container with foundation `FocusTrapElement`, and the managed
window root consults `active_focus_trap` when advancing focus. Dialog and Sheet already compose the
same semantic Overlay and therefore inherit the foundation trap. GPUI.NET retains its own viewport
placement, backdrop rendering, priority stack, topmost arbitration, focus restoration, and managed
dismissal callback routing.

Foundation `Sheet` is not used as the generic host because it couples Escape and backdrop closing,
has no independent `DismissOnEscape` option, and does not participate in GPUI.NET's priority stack.
Keeping those behaviors in the adapter preserves the semantic contract without adding ABI or
schema changes. Phase 4 is complete.

## Slider decision

GPUI.NET retains its native Slider resource. Foundation `SliderState` owns useful pointer geometry
and accessibility behavior, but it is configured only at construction and lacks GPUI.NET's focus,
keyboard, and keyboard-release semantics. Its current step quantization is relative to zero rather
than `min`, which differs for ranges whose minimum is not step-aligned. Replacing the retained
resource would therefore regress normal snapshot reconciliation and the public interaction event
contract.

The existing engine keeps stable `(session, owner View, key)` identity, reconciles min/max/step,
axis, scale, disabled state, and callback bindings, and preserves range/logarithmic mapping.
Pointer drag and keyboard stepping stay native; `SliderController.SetValue` remains event-free.
The retained root now supplies the same slider role, numeric value, bounds, step, and orientation
accessibility metadata used by the foundation primitive. No ABI or schema change is required.

## Input decision

GPUI.NET retains its native single-line Input resource. Foundation `InputState` provides a mature
Rope-backed editing engine with IME, Unicode, selection, clipboard, focus, read-only, disabled, and
password behavior. Its `InputEvent::Change` is intentionally only a notification, however, and its
public value accessor materializes the Rope into an owned string on every call. An adapter would
therefore need to copy the full value for every subscribed change and separately maintain the
revision carried by GPUI.NET's callback packet.

The existing engine keeps the current value in contiguous UTF-8 storage, suppresses duplicate
change notifications, and passes a borrowed value directly to the synchronous native callback.
The managed event copies those bytes for async safety and decodes UTF-16 only when `Value` is read.
It also preserves keyed retained identity, declarative-initial-value semantics, event-free
`SetValue`, native IME composition, grapheme selection, clipboard operations, password masking,
focus commands, and revisioned changed/submitted/focus events. The retained root now declares the
`TextInput` accessibility role used by the foundation frame. No ABI or schema change is required.

Richer editor behavior such as word navigation, undo/redo, and multiline editing remains separate
roadmap work rather than grounds to replace the stable single-line ABI contract.

## Scrolling and scrollbar decision

Scroll, List, and Table now use foundation `Scrollbar` for track and thumb geometry, painting,
hover/active state, track clicks, and drag lifecycle. Each adapter supplies a stable element ID and
projects the semantic scrollbar width into foundation track, thumb, inset, radius, and minimum
length styles. The application-wide foundation theme supplies the managed-authoritative track and
thumb colors. `ShowScrollbar(false)` still omits the scrollbar entirely, while the current public
contract continues to request an always-visible scrollbar when shown.

GPUI.NET retains the surrounding scroll resource and wheel path. Precise trackpad deltas still go
directly through GPUI; optional easing of discrete wheel deltas remains native and coalesced by
axis. `ScrollController` commands continue to mutate the retained `ScrollHandle` without managed
scroll callbacks. A small handle adapter preserves the declared two-pixel edge margin and gutter
placement while keeping the same maximum offset.

GPUI.NET seeds every native `ListState` row with the declared estimated item height. As visible rows
are rendered, GPUI replaces their hints with actual measurements and its sum tree updates the pixel
range. Foundation's direct list-handle mapping can therefore reach the full unmeasured range while
converging toward real content geometry, without rendering intermediate rows or crossing into
managed code. Reset and splice operations restore hints for new rows, while refresh operations
preserve the previous measured height as the remeasurement hint.

GPUI clears list measurements and hints when the viewport width changes because wrapping may alter
row height. A zero-paint native maintenance layer runs after list prepaint and restores uniform
hints before the sibling scrollbar reads the range. The remaining adapter only adjusts edge/gutter
geometry and cancels queued wheel easing on direct scrollbar input; it delegates offsets, maximum
range, track clicks, and drag lifecycle to `ListState`. No ABI, schema, or fork change is required.

## List and Table decision

GPUI.NET retains its existing List/Table engine. GPUI's native `ListState` owns variable-height
measurement and viewport state, while the GPUI.NET adapter owns aligned managed range batches,
stable model identity, content revisions, structural commands, and table column reconciliation.
Those responsibilities directly implement the managed crossing budget and remain authoritative.

Foundation `VirtualList` instead expects a Rust-owned range renderer and a complete vector of item
sizes. Adopting it would either move application data and sizing into Rust or rebuild the current
batching and reconciliation protocol around a less suitable layout primitive. That cost currently
has no corresponding API, behavior, or performance benefit. Foundation scrollbar and focus
behavior remain reusable independently. Performance benchmarks should continue to validate
GPUI.NET's retained engine, but comparison with `VirtualList` is no longer a migration gate. Reopen
the decision if the foundation implementation later offers a concrete advantage that justifies the
integration work.

## Dock decision and next advanced slice

Dock is the first advanced retained component probe. `DockArea(key, center)` establishes native
identity as `(session, owner View, key)`. Its center and optional left, bottom, and right regions are
structural declarations built from `DockRegion`, `DockSplit`, `DockTabs`, and stable-ID `DockPanel`
nodes. Panel content remains an ordinary semantic subtree and may include framework-owned child
Views and their retained resources. The native adapter updates content and panel presentation on
every dirty render but rebuilds only the affected foundation layout when axes, initial sizes,
active indices, panel IDs, region placement, or container structure change. Consequently clean
frames do not call managed code, and ordinary managed invalidation does not undo native tab moves,
split sizes, or side-region open state.

The current slice deliberately has no Dock command or event ABI. Persistence, tiles, programmatic
layout operations, close/layout-change events, and application reconciliation policy must be
designed together rather than exposing foundation entities piecemeal. Pointer dragging, resizing,
region collapse, drop targeting, focus, and frame-sensitive layout remain native.

Rich Editor is separate from the existing single-line Input and from the default package graph.
The base schema has one generic NativeExtension envelope; `Gpui.Editor` owns the typed managed
schema, while `gpui-dotnet-editor-host` links the matching provider into a custom host. ABI version
3 negotiates the editor ID, version, and schema hash before startup and routes schema-owned commands.
The provider retains the native Rope and frame-sensitive editing state. It supports one-shot
bootstrap, revisioned UTF-8 delta events, focus, revision-checked selection, whole-document
replacement, one contiguous edit, and explicit stale/range rejection events. This deliberately
small surface validates the generic extension contract; editor-specific depth such as undo/redo,
highlighter reconfiguration, and persistence remains optional roadmap work.

## Default native host size and dependency boundary

Optional managed assemblies are not sufficient isolation when the default Rust host still links a
broad implementation crate. The default host should contain GPUI.NET Core behavior and only the
native component code needed by its supported semantic surface. Editor providers, language
grammars, and other optional component families belong only in custom hosts that select them.

An equivalent locked Windows x64 Release build established these reference points:

| Native host boundary | `gpui_dotnet.dll` size |
|---|---:|
| Before the foundation migration | 13,194,240 bytes |
| Initial `gpui-base` adoption | 13,557,760 bytes |
| Current semantic surface immediately before styled Dock integration | 14,127,104 bytes |
| Styled Dock integration through the monolithic `gpui-component` crate | 19,888,640 bytes |
| Styled Dock in the current default host | 19,929,600 bytes |
| Local Dock skin on `gpui-base` only | 14,988,800 bytes |

The large boundary was the styled Dock integration, not the initial `gpui-base` adoption and not
the optional Editor host. Dock made the default host reference `gpui-component` and its
asset crate. The resulting reachable graph included substantially more component, theme,
Markdown, regular-expression, and parsing code than the Dock contract needs. Bundled component
assets were a small fraction of the increase. Replacing the full component initializer with minimal
global initialization also produced only a small reduction, so initialization alone was not the
solution.

The Dock skin is now a small in-repo renderer over `gpui-base` (`crates/gpui-dotnet/src/dock_skin.rs`).
The default host resolves one GPUI type universe through `gpui` plus `gpui-base`, ships no
`gpui-component` skin crate and no bundled asset provider (the application runs with empty asset
resolution), and projects the managed theme only into the foundation theme. Broad
`gpui-component` facilities remain available to custom native hosts that require them, such as the
optional editor host. A Windows x64 Release build of the split host
(`cargo build -p gpui-dotnet-default-host --release`) measures 14,988,800
bytes, against 14,127,104 bytes at the pre-Dock reference and 19,929,600 bytes with the styled
integration: the split recovered most of the roughly 5.8 MB regression, and the remaining roughly
0.9 MB is the retained Dock engine and the local skin itself. The structural direction below is in
place; what remains is recorded measurement rather than further isolation:

- retained Dock behavior and layout engine stays in `gpui-base`;
- the Dock skin is a small renderer that does not depend on the complete `gpui-component` facade;
- broad `gpui-component` facilities are enabled only in custom native hosts that require them;
- keep the default host's Cargo feature set explicit and additive, with no accidental provider
  activation through default features;
- inspect release link maps or equivalent size reports so an optional family cannot silently enter
  the default host.

Release-profile optimization is complementary, not a substitute for dependency isolation. On the
same Windows build, Thin LTO with one codegen unit and symbol stripping reduced the current host to
18,287,616 bytes. Adopt those settings only after build-time and runtime-performance validation;
they do not remove the roughly 5.8 MB dependency-boundary regression.

The size work is complete when CI records enough artifact information to detect renewed
dependency leakage. The binary target is met: an equivalent Windows x64 Release build keeps the
default host at 14,988,800 bytes, near the 14,127,104-byte pre-Dock reference, with Dock behavior
retained and the optional Editor available from its custom host. Until CI recording lands, the
boundary is guarded structurally: the default host must resolve
no `gpui-component` reverse dependency (`cargo tree --locked --manifest-path
crates/gpui-dotnet/Cargo.toml -p gpui-dotnet-default-host --invert gpui-component` matches no
package) while `gpui-base` remains its foundation.

The remaining protocol questions stay open for later families:

| Area | Decision to record |
|---|---|
| Semantic IR | Whether operation-oriented records remain appropriate or component families need stronger typed semantic nodes. |
| Native identity | Whether retained resources should evolve into a broader component-state registry keyed by session, owner View, stable key, and type. |
| Events | Whether controls should emit a common typed semantic event packet instead of family-specific callback paths. |
| Commands | Whether retained commands should use family-specific packets or a common validated envelope. |
| Schema | Whether it should describe nodes, events, commands, and wire constraints as separate compatibility units. |
| API table | Which operations are host capabilities and which belong in ordinary protocol packets. |
| Versioning | Whether host API, render IR, events, and commands require independent compatibility versions. |

Preserve these invariants through any redesign:

- managed rendering remains coarse-grained and retry-safe;
- clean native repaint does not invoke managed `Render()`;
- high-frequency pointer, scrolling, focus, IME, measurement, and animation state stays native;
- native identity survives ordinary managed rerenders;
- pointer/length pairs and value domains are validated before use;
- Rust panics and managed exceptions never cross FFI;
- Rust object layouts and foundation types never cross the ABI.

## Fork and upstream workflow

The `external/gpui-component` submodule uses:

- `origin`: `akeit0/gpui-component`, the GPUI.NET integration fork;
- `upstream`: `longbridge/gpui-component`, the authoritative upstream.

Keep fork `main` synchronized with upstream while the downstream delta is zero. Create an
integration branch when the first fork-only patch is required. GPUI.NET pins the exact submodule
commit, never a branch.

Classify every discovered change before editing the fork:

| Change | Owner |
|---|---|
| Generic `gpui-base` bug fix or reusable API | Fork temporarily, test there, then submit upstream. |
| GPUI.NET ABI, FFI, callback, schema, or managed-runtime behavior | GPUI.NET repository only. |
| Generic lower-level GPUI issue | Zed/GPUI upstream; use a temporary fork only for a concrete blocker. |

Record every fork-only patch, reason, test coverage, upstream issue or pull request, and removal plan
in [UPSTREAM_BASELINE.md](UPSTREAM_BASELINE.md).

## Migration acceptance criteria

A behavior family is migrated only when:

1. foundation behavior covers the required semantics or remaining differences have a documented
   technical rationale;
2. managed APIs stay idiomatic and independent of Rust implementation details;
3. identity and reconciliation preserve native state across normal rerenders and theme changes;
4. semantic events and controller commands are tested across the boundary;
5. pointer, keyboard, focus, accessibility, disabled, and controlled-state behavior is covered;
6. superseded native behavior is removed;
7. any fork patch is recorded and has an upstream disposition;
8. managed/native traffic and clean repaint behavior do not regress.

Input, deferred layers, scrolling, and virtualization additionally require focused interaction or
performance validation appropriate to their risk.

## Verification

Run targeted tests while iterating. Before completing a normal migration slice, run:

```sh
dotnet run --project tools/Gpui.Bindings.Generator -- verify
cargo fmt --manifest-path crates/gpui-dotnet/Cargo.toml -- --check
cargo tree --locked --manifest-path crates/gpui-dotnet/Cargo.toml --invert gpui
cargo test --locked --manifest-path crates/gpui-dotnet/Cargo.toml
dotnet test Gpui.slnx --no-restore
dotnet build samples/Gpui.Sample/Gpui.Sample.csproj --no-restore
git diff --check
```

For UI behavior, launch the sample on the affected platform and exercise initial state plus relevant
transitions. Record only platforms actually tested; CI configuration is not runtime verification.

## Maintaining this document

- Update the phase table and next development slice in the same change that advances migration.
- Describe current implementation ownership, not session chronology.
- Keep exact revisions and downstream patch details in `UPSTREAM_BASELINE.md` and
  `crates/native-baseline.toml` rather than duplicating them here.
- Put unrelated open product work in `NEXT_STEPS.md`.
- Remove superseded instructions instead of accumulating compatibility-era notes.
