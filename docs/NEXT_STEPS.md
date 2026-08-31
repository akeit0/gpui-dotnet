# Next steps

The repository now has framework-owned dynamic managed Views, ABI v4 typed native-control events, retained native Scroll and Input state, virtualized ListState, deferred window Overlay layers, batched managed datasource rendering, lifecycle hooks, capability-based semantic bindings, native adapters and flat snapshots.

## 1. Input hardening and Editor

The single-line Input establishes the native-entity ABI for focus, IME, selection, subscriptions, UTF-8 values, and imperative commands. Harden it before expanding the same foundation into a multiline Editor:

- accessibility role/value/selection exposure and platform verification;
- word navigation, undo/redo, double/triple-click selection, and richer platform keymaps;
- validation/error/help presentation without moving edit state into managed renders;
- composition/IME tests across Windows, macOS, and Linux;
- optional controlled-value semantics with explicit revision/conflict behavior.

## 2. Overlay compositions

The generic window-relative Overlay is the geometry and event foundation. Build semantic compositions on it rather than repeatedly recreating modal behavior:

- Dialog and Sheet with a complete tab/shift-tab focus trap and deterministic nested-overlay focus restoration;
- Popover/Menu with trigger measurement, anchor flipping/snapping, keyboard navigation, and outside-click dismissal;
- Toast/notification host with retained ordering, timeouts, pause-on-hover, and reduced-motion policy;
- accessibility roles, labels, focus announcements, and platform verification.

Keep stacking and dismissal window-owned; keep product styling and content composition in managed code.

## 3. List datasource refinement

The current List cache is correctness-first: any managed root snapshot revision drops managed row snapshot batches while retaining `ListState`. Future optimization can add an explicit datasource/content revision so unrelated parent renders do not invalidate row snapshots.

Also evaluate:

- uniform-height fast path behind the same `ui.List` API;
- stable model-ID payload helpers;
- batched multi-range invalidation;
- cache/overscan telemetry;
- dedicated item-scoped retained state if real applications prove it necessary.

Do not expose GPUI's uniform/variable implementation split directly unless it provides durable semantic value.

## 4. Table/tree

Build Table/Tree on the same coarse datasource principles rather than as thousands of managed child Views. Column/tree metadata can be semantic IR; viewport state and measurement remain native.

## 5. Multi-window application model

Move from the single convenience root toward an application abstraction that can create multiple managed windows while retaining app-level native initialization/services.

## 6. Package and ABI CI

Add CI gates for:

- all supported native RID builds;
- Linux/macOS native RID builds in addition to the current Windows build;
- NuGet consumer tests without a Rust toolchain;
- ABI C header/layout assertions.
