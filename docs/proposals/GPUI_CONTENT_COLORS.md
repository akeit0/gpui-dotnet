# GPUI proposal: inherited content colors with source-aware foregrounds

Status: unimplemented upstream proposal. No issue or pull request has been submitted by this
repository. GPUI.NET keeps GPUI unmodified and currently supports only its single inherited
foreground. This document records a capability worth discussing upstream if other GPUI
applications need it; it does not establish demand or agreement from the Zed maintainers.

## A reusable native UI problem

A selectable row or button can contain a title and a secondary count. The owner chooses its
normal, hover, and pressed background/content combinations. The count should request secondary
emphasis relative to that owner's surface, including when the background becomes an accent
color. A fixed application-wide muted gray may be unreadable there.

This is independent of C#, an FFI, or GPUI.NET. Native Rust components, reusable composite
controls, retained child views, and virtualized rows can encounter the same problem. A useful
minimal demonstration keeps the primary color constant while secondary alone changes during
native hover and press. A single inherited foreground cannot represent that case.

Applications that need only one foreground should keep ordinary text inheritance. A second
role is justified only when both colors must vary independently and manual child palette
plumbing is becoming a real component-composition problem.

## Evidence in the pinned baseline

These observations concern Zed revision `f66ed399cdde86092af8af3dc7b418abf45f37f8`, recorded in
[the native baseline](../UPSTREAM_BASELINE.md). They are source inspection, not a claim about
current upstream main or platform/GPU verification.

| Source | Observation |
| --- | --- |
| [TextStyle and TextStyleRefinement](https://github.com/zed-industries/zed/blob/f66ed399cdde86092af8af3dc7b418abf45f37f8/crates/gpui/src/style.rs) | TextStyle has one resolved `color`; its derived refinement holds an optional literal color. No inherited two-role content palette is present. |
| [Styled::text_color](https://github.com/zed-industries/zed/blob/f66ed399cdde86092af8af3dc7b418abf45f37f8/crates/gpui/src/styled.rs) | The setter writes that literal refinement. |
| [Div interaction and inheritance](https://github.com/zed-industries/zed/blob/f66ed399cdde86092af8af3dc7b418abf45f37f8/crates/gpui/src/elements/div.rs) | GPUI owns hover/active refinement selection and scopes text styles during element rendering. Pending keyboard activation and pointer clicked state are separate. Activation support alone does not prove held-key active paint. |
| [Window text and deferred drawing](https://github.com/zed-industries/zed/blob/f66ed399cdde86092af8af3dc7b418abf45f37f8/crates/gpui/src/window.rs) | The text-style stack supplies inheritance and is captured by deferred draws, including replay paths. |
| [Text-run creation](https://github.com/zed-industries/zed/blob/f66ed399cdde86092af8af3dc7b418abf45f37f8/crates/gpui/src/elements/text.rs) | Text styling feeds shaping and color-bearing runs; changing an unrelated paint context afterward cannot reliably recolor those runs. |

## Proposed scope

Extend GPUI's existing inherited text-style path with a fixed, complete pair of Primary and
Secondary colors, plus a foreground source: Inherit, Primary, Secondary, or Literal(color).
Names and concrete Rust API signatures need upstream review. This is a behavioral contract,
not a request to adopt GPUI.NET operation IDs or application variant names.

Keep the resolved `TextStyle.color` available to existing text consumers. The authored source
must occupy one logical refinement slot: adding an unrelated role field with a fixed priority
over `color` would break declaration order. Literal setters, direct literal initializers,
refinement merging, and role setters must agree on that slot.

1. Fold an element's authored declarations into each existing state refinement in order.
2. Let GPUI select and merge the element's native interaction refinements.
3. Resolve the merged refinement once against the incoming parent content context, before
   shaping or other color-consuming work.

An explicit Inherit in the final refinement refers to the parent, not to an earlier base-layer
literal on the same element. Ordinary descendants inherit the actual resolved parent foreground.
Explicit Primary or Secondary selects that role from the nearest complete palette, even if an
intermediate ancestor overrides only its ordinary foreground with a literal.

The palette and foreground have distinct responsibilities:

- Publishing a complete palette replaces both role colors, including zero-alpha values.
- A convenience content/surface declaration may publish the pair and select Primary in order.
- A literal text-color setter changes only the foreground source, not the palette.
- Inherit cancels the local foreground selector without removing a separately declared palette.
- A raw background setter remains paint-only; it must not infer a palette.
- Native defaults may supply a palette without changing legacy inherited foreground behavior.

Required order examples:

| Local declarations | Result |
| --- | --- |
| Secondary, then literal red | Red. |
| Literal red, then Secondary | Nearest palette's Secondary. |
| Content pair, then literal red | Ordinary foreground red; both role values remain available. |
| Literal red, then content pair selecting Primary | New Primary. |
| Content pair, then Inherit | Parent's actual foreground; local role pair remains available to explicit role consumers. |

No state bits should cascade to descendants. Independent controls retain their own interaction
and component boundaries. Checkbox/Radio labels may inherit surrounding content while their
indicators remain separate parts. Placeholder, caret, selection, slider parts, syntax colors,
raster tinting, and Drawing brushes are separate capabilities, not extra ambient role names.

## Lifetime, caching, and cost

Carry the content values through the existing window-owned text-style scope and deferred capture
paths. Do not create a process-global palette, per-leaf registry, or second pointer-state engine.
Cached view replay and virtualized child insertion need the same semantics as fresh children.
Custom components participate only when they use the inherited context; arbitrary literal paints
cannot be automatically transformed.

Resolve colors before creating text runs. Invalidate or refresh color-bearing caches when
content changes, including when only Secondary changes. Preserve geometry reuse for paint-only
changes where possible, and measure shaping and allocation costs rather than assuming them.
Open deferred layers must refresh their frame capture when reused; popup bodies can establish
their own component surfaces instead of inheriting a trigger's hover palette indefinitely.

Use fixed inline values and bounded frame-owned storage. Avoid a second descendant traversal,
per-role consumer listeners, historical scope retention, and per-row factory closures just to
recolor text. No layout-changing hover features are necessary for this proposal.

## Alternatives and adoption decision

GPUI.NET currently resolves matching background/foreground pairs in typed application recipes.
Explicit secondary child colors are checked against all owner backgrounds. Inherited text with
typographic emphasis works when both emphasis levels can share the same changing foreground.
These approaches use existing GPUI and remain appropriate for small composites.

Compiling ancestor palettes into consumer group-hover/group-active styles is another possible
prototype. It adds per-consumer state wiring and scope identity, complicates nested controls,
keyboard activation, and cached/deferred insertion, and must be measured against normal
inheritance. GPUI.NET does not implement this alternative as a replacement styling engine.

Upstream implementation should proceed only if maintainers consider the capability useful beyond
one binding and can support its compatibility and performance costs. If accepted upstream,
GPUI.NET can adopt a validated revision, add semantic role declarations, and migrate contextual
child styles. Until then, no public role API or dependency fork is planned.

## Acceptance criteria for an upstream implementation

- A real nested title/count composite changes only Secondary on parent hover and press, with
  primary fixed. Inspect the colors consumed by text shaping/painting, not only style records.
- Exercise pointer enter/exit, press, capture, release outside, cancellation, focus movement,
  held Enter/Space, repeat, and key cancellation. Separately resolve the pinned keyboard active
  paint gap if the API promises keyboard/pointer parity.
- Cover literal/role declaration order, explicit Inherit after state merging, transparent
  colors, local palette replacement, explicit child overrides, and independent nested controls.
- Cover retained child reuse, virtualized row insertion/eviction, deferred scheduling/replay,
  open-layer theme updates, close, and owner retirement without stale colors or captured owners.
- Preserve Input editing/IME/selection/focus, Slider value/drag state, and collection/Dock state
  when presentation changes. Preserve legacy literal inheritance and component parts.
- Measure clean interaction renders, row requests, shaping, allocations, and scope retention;
  the extension must not require application rerenders or row requests purely to change colors.
- Verify native builds and actual input/rendering on supported desktop platforms. Contract or
  headless tests do not establish GPU, IME, accessibility, or platform behavior.
