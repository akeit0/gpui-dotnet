# AGENTS.md

These instructions apply to the entire repository.

## Repository purpose

GPUI.NET is a semantic C# frontend for Rust GPUI. C# owns application state, managed Views, themes,
and product-specific style variants. Rust owns GPUI, native windows, validation, retained controls,
virtualization, deferred layers, and frame-sensitive interaction.

Do not turn the project into a one-to-one P/Invoke mirror of GPUI. Prefer one semantic declaration,
coarse data batches, and native retained state over per-property, per-frame, or per-row crossings.

## Start here

Read the relevant focused document before changing a subsystem:

- `README.md`: setup and public API overview
- `docs/ARCHITECTURE.md`: ownership and render/resource paths
- `docs/COMPONENTS.md`: component classes and authoring workflow
- `docs/VIEW_LIFECYCLE.md`: child slots, render purity, and teardown
- `docs/THREADING.md`: GPUI entities, managed lifecycle, async continuations, and thread boundaries
- `docs/HOT_RELOAD.md`: managed metadata-update and renderer refresh contract
- `docs/ABI.md`: C layouts, validation, commands, and callbacks
- `docs/BINDING_GENERATION.md`: generated code ownership
- `docs/PERFORMANCE.md`: crossing and allocation constraints
- `docs/PACKAGING.md`: package graph and native assets

## Source-of-truth rules

`bindings/schema.json` is the source of truth for semantic components, operations, capabilities,
payload constraints, native adapters, and the managed/native schema hash.

After changing it, run:

```sh
dotnet run --project tools/Gpui.Bindings.Generator -- generate
dotnet run --project tools/Gpui.Bindings.Generator -- verify
```

Never hand-edit:

- `src/Gpui/Rendering/Semantic.g.cs`
- `crates/gpui-dotnet/src/semantic.g.rs`
- `src/Gpui/Interop/NativeMethods.g.cs`

The native Cargo build generates `NativeMethods.g.cs` from Rust C-layout definitions through
`csbindgen`. Include that generated change whenever `crates/gpui-dotnet/src/abi.rs` changes.

## Cross-platform behavior

Changes must add or preserve support rather than replacing one platform path with another.

- Keep the Windows managed/app-side title-bar and menu implementation working.
- Keep the native macOS system title bar, traffic lights, and global application menu working.
- Keep forced managed title/menu composition available on macOS.
- Keep manual title-bar composition possible on every platform.
- Put platform-specific window behavior in Rust or narrowly scoped managed platform selection; do
  not scatter `OperatingSystem.IsMacOS()` branches through sample views.
- Do not claim a platform is verified unless its native build or behavior was actually checked.

## Themes and styles

`GpuiTheme` is application-wide ambient render input. Theme changes must update:

- every retained managed child fragment;
- virtual List/Table row batches;
- native component defaults and retained controls;
- window/root inherited colors.

Native theme payloads contain resolved semantic roles only. Product variants such as Primary,
Danger, Navigation, or Compact belong in the application and should use
`IGpuiElementStyle<TTag>`. Do not add a fixed library style definition for every component variant.

Prefer inherited container text/background roles over repeated leaf styling, while preserving
explicit component states and contrast.

## Managed View rules

`Render()` and `[GpuiListItem]` methods may be retried after native arena growth. They must be
deterministic and must not mutate application state, perform I/O, start tasks, call controllers, or
invalidate the View.

Event bindings and ref-bound controller key initialization are supported render-time declarations.
Perform state changes in event or lifecycle methods and then call `Invalidate()`.

Use framework-owned `ui.Child<T>()` slots. Use `View<TProps>` for parent-owned render inputs; props
must be supplied on every declaration. Use stable keys for conditional, repeated, or reorderable
children.

Props must implement `IEquatable<TProps>` through the `View<TProps>` generic constraint. Prefer
records or record structs; do not weaken the constraint and silently reintroduce boxed comparisons.

View lifetime follows framework ownership: a window owns its root and a committed parent slot owns
its child. A CLR reference does not retain UI ownership. Unmount is terminal, so do not add remount
semantics or implicit/manual child retention. `OnUnmounted()` is cleanup-only: its `Lifetime` is
already cancelled and runtime commands are unavailable.

Keep one-shot View identity separate from mounted runtime state. Any-thread operations must use the
stable, non-pooled command route and must not inspect the GPUI-thread-only attachment. Remove and
fully reset that attachment before user unmount cleanup; only the attachment may be pooled.

Virtual rows are element-only cached snapshots. Do not add mounted child Views, nested retained
resources, or deferred layers inside a row renderer.

## Native and ABI rules

- Validate all pointer/length pairs and reserved fields before dereference.
- Prevent Rust panics and managed exceptions from crossing FFI.
- Keep fixed-width C layouts synchronized and covered by tests.
- Execute GPUI mutations on the native application thread.
- Preserve retained resource identity as `(session, owner View, UTF-8 key)`.
- Reconcile commands with declarative snapshots; the snapshot is authoritative.
- Keep high-frequency pointer, scroll, IME, focus, and measurement state native.
- Batch reverse datasource calls and avoid per-row managed closures.

An ABI layout or entry-point change requires Rust and managed contract updates, generated bindings,
tests, and `docs/ABI.md`. A semantic-only schema change normally keeps ABI version 1 and changes the
schema hash.

## Editing and formatting

- Preserve unrelated user changes in a dirty worktree.
- Use `rg`/`rg --files` for discovery.
- Use `apply_patch` for source and documentation edits.
- Follow existing C# formatting and `cargo fmt` for Rust.
- Keep nullable analysis and warnings-as-errors clean.
- Avoid allocations and string formatting in native row/render hot paths.
- Keep comments focused on invariants and non-obvious ownership, not development history.

Documentation must describe the current repository for a new reader. Put only genuinely open work
in `docs/NEXT_STEPS.md`; do not add session history, completed checklists, dated review notes, or
temporary diagnosis to README/docs.

## Verification

Run targeted tests while iterating. Before handing off a normal code change, run the applicable
full checks:

```sh
dotnet run --project tools/Gpui.Bindings.Generator -- verify
cargo fmt --manifest-path crates/gpui-dotnet/Cargo.toml -- --check
cargo test --manifest-path crates/gpui-dotnet/Cargo.toml
dotnet test Gpui.slnx --no-restore
dotnet build samples/Gpui.Sample/Gpui.Sample.csproj --no-restore
git diff --check
```

For documentation-only changes, verify links, command names, generated-file names, and
`git diff --check`; run compilation only if examples or build metadata changed.

For UI behavior, build and launch the sample on the affected platform. Exercise both initial state
and transitions such as theme switching, resizing, scrolling, focus, route replacement, title-bar
mode, and menu activation. Do not report visual verification when screen access was unavailable.

## Git

Do not discard or rewrite user work. Avoid destructive reset/checkout commands. Commit only when the
user asks, and report the resulting commit hash after confirming the intended branch points to it.
