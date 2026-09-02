# gpui-base migration

This is the living development plan and status tracker for adopting `gpui-base` as GPUI.NET's
native behavior foundation. Keep it aligned with the current implementation. Exact dependency
revisions and the downstream patch ledger live in [UPSTREAM_BASELINE.md](UPSTREAM_BASELINE.md).

## Objective

Reuse `gpui-base` for native interaction, focus, accessibility, controlled state, text editing,
popups, scrolling, motion, and other broadly useful behavior while preserving GPUI.NET's semantic
managed/native boundary.

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
        ├── gpui-base reusable behavior
        ├── direct GPUI primitives
        └── platform-specific integration
```

`gpui-base` is an implementation foundation, not part of the managed public contract. C# APIs do
not expose Rust implementation types.

## Current status

Status values are `Complete`, `In progress`, `Planned`, and `Decision pending`.

| Phase | Status | Current state | Exit condition |
|---|---|---|---|
| 0. Forked native baseline | Complete | The fork is pinned to an exact SHA, the compatible GPUI revision is locked, CI/builds use `--locked`, and the graph guard resolves one GPUI package. | The dependency tuple is deterministic and documented. |
| 1. Initialization and theme bridge | Complete | `gpui_base::init(cx)` runs before queued window creation. The version 2 native theme payload carries explicit appearance, and startup plus every theme update project managed semantic roles into the foundation theme. | Existing and foundation-backed controls use the same managed-authoritative theme source. |
| 2. Button, Checkbox, and Radio probe | Complete | Root and virtual-row adapters use foundation primitives with stable identity, derived accessible names, controlled checked state, disabled behavior, native focus traversal, and existing click dispatch. | The family is foundation-backed with pointer, keyboard, focus, accessibility, disabled, and controlled-state parity. |
| 3. Protocol checkpoint | Complete | The snapshot and click protocols remain; the schema adds a narrow `disableable` capability and Boolean `Disabled` operation. | Findings from the first control family produce an explicit keep/change decision with tests and updated ABI documentation. |
| 4. Deferred layers | Complete | Tooltip and PopoverMenu use foundation Popup/Positioner, ContextMenu uses Positioner, and PopoverMenu plus ContextMenu use PopoverState. Modal Overlay uses foundation FocusTrapElement, which also covers the managed Dialog and Sheet compositions. GPUI.NET retains its distinct timing, placement, backdrop, priority, topmost, and managed callback semantics. | Foundation behavior replaces duplicated placement, focus, and dismissal infrastructure where semantics match. |
| 5. Slider | Complete | GPUI.NET retains its native Slider engine because the foundation state cannot reconcile configuration and lacks keyboard/focus plus released-event parity. The retained root now exposes foundation-equivalent slider accessibility metadata. | Foundation behavior reaches pointer/keyboard/controller/event parity, or the custom implementation is retained with rationale. |
| 6. Input | Complete | GPUI.NET retains its single-line editing engine because it preserves the revisioned contiguous UTF-8 event path without per-event native value materialization. The retained root now exposes the foundation-equivalent text-input role. | IME, Unicode, selection, clipboard, focus, commands, revisions, and events reach parity before old behavior is removed. |
| 7. Scrolling and scrollbar | Planned | GPUI.NET retains scrolling and scrollbar behavior. | Foundation scrollbar behavior is adopted selectively without additional managed/native traffic. |
| 8. List and Table evaluation | Decision pending | The existing aligned range-batch cache remains authoritative. | Benchmarks record an explicit keep or migrate decision. |
| 9. Cleanup and protocol freeze candidate | Planned | Compatibility remains preview-level. | Superseded behavior and protocol paths are removed and the new contract is deliberately reviewed for stability. |

No semantic component is considered migrated merely because the dependency and initializer exist.
Implementation ownership changes only after the component-specific exit criteria pass.

## Foundation theme bridge

`GpuiTheme` remains the application-wide source of truth. The native adapter maps resolved
`NativeTheme` roles into `gpui_base::Theme`; foundation types do not cross the managed API or ABI.

The private version 2 theme payload carries explicit `Light` or `Dark` appearance rather than
inferring it from colors. The projection supplies foundation color tokens, scrollbar paint,
resizable-handle colors, and selection color. Foundation typography, radius, spacing, shadow,
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

## Next development slice: Scrolling and scrollbar

Compare the retained Scroll and scrollbar implementation with foundation behavior. Adopt reusable
native scrollbar interaction and motion where it preserves the current controller, viewport,
event, nested-scroll, and native-only high-frequency paths without increasing managed/native
traffic.

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
