# Next steps

The repository now has framework-owned dynamic managed Views, ABI v3 event payloads, retained native Scroll state, virtualized ListState, batched managed datasource rendering, lifecycle hooks, capability-based semantic bindings, native adapters and flat snapshots.

## 1. Input/editor native entities

Use an input/editor as the first opaque native-entity implementation. It must establish the long-lived control ABI for focus, IME, selection, subscriptions and text-change events without a managed callback per keystroke/frame.

## 2. List datasource refinement

The current List cache is correctness-first: any managed root snapshot revision drops managed row snapshot batches while retaining `ListState`. Future optimization can add an explicit datasource/content revision so unrelated parent renders do not invalidate row snapshots.

Also evaluate:

- uniform-height fast path behind the same `ui.List` API;
- stable model-ID payload helpers;
- batched multi-range invalidation;
- cache/overscan telemetry;
- dedicated item-scoped retained state if real applications prove it necessary.

Do not expose GPUI's uniform/variable implementation split directly unless it provides durable semantic value.

## 3. Table/tree

Build Table/Tree on the same coarse datasource principles rather than as thousands of managed child Views. Column/tree metadata can be semantic IR; viewport state and measurement remain native.

## 4. Multi-window application model

Move from the single convenience root toward an application abstraction that can create multiple managed windows while retaining app-level native initialization/services.

## 5. NativeAOT and package CI

Add CI gates for:

- all supported native RID builds;
- semantic generation verify;
- managed source-generator sample compilation;
- NativeAOT smoke applications;
- NuGet consumer tests without a Rust toolchain;
- ABI C header/layout assertions.
