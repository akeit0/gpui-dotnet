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
| 1. Initialization and theme bridge | In progress | `gpui_base::init(cx)` runs before queued window creation. Theme projection and a foundation-backed render probe remain. | Existing and foundation-backed controls use the same managed-authoritative theme source. |
| 2. Button, Checkbox, and Radio probe | Planned | Existing adapters still use GPUI.NET/direct GPUI behavior. | The family is foundation-backed with pointer, keyboard, focus, accessibility, disabled, and controlled-state parity. |
| 3. Protocol checkpoint | Planned | The current arena, event, command, and identity protocols remain in place. | Findings from the first control family produce an explicit keep/change decision with tests and updated ABI documentation. |
| 4. Deferred layers | Planned | Tooltip, PopoverMenu, ContextMenu, Overlay, Dialog, and Sheet retain their current implementations. | Foundation behavior replaces duplicated placement, focus, and dismissal infrastructure where semantics match. |
| 5. Slider | Planned | GPUI.NET retains slider interaction and state. | Foundation behavior reaches pointer/keyboard/controller/event parity, or the custom implementation is retained with rationale. |
| 6. Input | Planned | GPUI.NET retains its single-line editing engine. | IME, Unicode, selection, clipboard, focus, commands, revisions, and events reach parity before old behavior is removed. |
| 7. Scrolling and scrollbar | Planned | GPUI.NET retains scrolling and scrollbar behavior. | Foundation scrollbar behavior is adopted selectively without additional managed/native traffic. |
| 8. List and Table evaluation | Decision pending | The existing aligned range-batch cache remains authoritative. | Benchmarks record an explicit keep or migrate decision. |
| 9. Cleanup and protocol freeze candidate | Planned | Compatibility remains preview-level. | Superseded behavior and protocol paths are removed and the new contract is deliberately reviewed for stability. |

No semantic component is considered migrated merely because the dependency and initializer exist.
Implementation ownership changes only after the component-specific exit criteria pass.

## Next development slice: theme projection

`GpuiTheme` remains the application-wide source of truth. Implement one native adapter that maps
the resolved `NativeTheme` roles into `gpui_base::Theme`.

Required work:

1. Define the projection in `crates/gpui-dotnet/src/theme.rs` without exposing foundation types to
   managed code or the ABI.
2. Decide how light/dark appearance is represented. The version 1 native payload currently carries
   resolved colors but no explicit appearance field; do not silently infer a compatibility contract
   from a foundation implementation type.
3. Map the color and scrollbar roles available in `NativeTheme`. Keep foundation typography,
   radius, spacing, and shadow defaults until GPUI.NET deliberately adds corresponding semantic
   roles to its versioned theme protocol.
4. Apply the initial projection during native startup after `gpui_base::init(cx)`.
5. Apply the same projection for every `SetTheme` application command before refreshing windows.
6. Preserve retained resource and component identity across theme updates.
7. Add tests for appearance handling and representative color and scrollbar roles supported by the
   current native payload.
8. Render a minimal foundation-backed control in a native test or focused adapter probe to prove
   it observes the projected theme.

The slice is complete when existing native defaults and the probe observe one managed-authoritative
theme update without recreating retained state.

## First behavior probe

After theme projection, migrate Button, Checkbox, and Radio as one design probe. Preserve the
managed authoring API unless integration evidence justifies a better semantic contract.

Validate:

- stable native identity and deterministic element IDs;
- pointer activation and Enter/Space keyboard behavior;
- focus traversal and focus-visible behavior;
- accessibility roles, labels, actions, checked state, and disabled state;
- controlled Checkbox and Radio transitions;
- click/event translation without raw GPUI events crossing the ABI;
- native hover, active, checked, selected, and disabled style projection;
- no new per-frame or high-frequency managed/native traffic;
- removal of superseded GPUI.NET behavior after parity is established.

If the probe exposes a protocol mismatch, stop scaling component migration and complete the Phase 3
protocol checkpoint first.

## Protocol checkpoint questions

The implementation may change preview ABI and semantic protocol shapes when migration evidence
supports a cleaner model. Review these areas after the first behavior probe:

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

The local `C:\Users\akito\gpui-component` clone uses:

- `origin`: `akeit0/gpui-component`, the GPUI.NET integration fork;
- `upstream`: `longbridge/gpui-component`, the authoritative upstream.

Keep fork `main` synchronized with upstream while the downstream delta is zero. Create an
integration branch when the first fork-only patch is required. GPUI.NET always pins an exact commit,
not a branch.

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
