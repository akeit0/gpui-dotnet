# Wander findings — mobile-style gaps

Built while writing `samples/Gpui.Wander`. Complements `TaskBoard/FINDINGS.md`;
items here are specific to web/mobile-style composition.

## What worked well

- A phone column + bottom tab bar composes entirely from `Div`/`HStack`/`VStack`,
  buttons, badges, and one keyed child slot. No tab primitive was missed.
- Bottom `Sheet` detail panels feel native with zero animation code.
- Silent like writes + `RefreshRanges` kept 60fps-style targeted updates without
  touching the revision — the intended pattern works as documented.
- Props-bearing row methods (`in TProps`) give rows clean access to accepted
  inputs; the generator diagnostic (GPUI012) fires precisely when the arity is
  wrong.

## Gaps

1. **No pull-to-refresh.** Refresh is a toolbar button driving `WorkScope`; a
   native overscroll gesture with a spinner has no contract. Mobile feeds want
   this as a retained behavior, not an app button.
2. **Overlays are window-relative only.** Sheets/dialogs center on the window
   viewport, not on the phone column. A mobile layout wants container-anchored
   layers (sheet slides within the phone frame); today the sheet spans the
   desktop window.
3. **No safe-area / notch API.** The status spoof is hand-drawn text. Real
   mobile hosts need inset queries; desktop windows do not — but a "mobile
   style" sample cannot be faithful without them.
4. **No haptic, share-sheet, or image-picker contracts.** Liking, sharing, and
   avatar picking are buttons with no native follow-through. URI image loading
   is already noted as absent in `COMPONENTS.md`; pickers/share are the same
   class of gap.
5. **Tab state does not survive switching.** One keyed slot replacing the child
   type unmounts the old tab, resetting its scroll position. iOS-style retained
   tabs would need all tabs mounted with visibility control; there is no
   visible/hidden toggle that keeps native state without painting cost.
6. **Silent writes are a handshake, not a helper.** `ToggleEntryLike` + manual
   `RefreshRanges` + manual `Invalidate` for header counts is three coordinated
   calls. A small "mutate row range" helper (write + refresh + invalidate) would
   remove a bug-prone pattern.
7. **`contentRevision` must cover filter state, not just store mutations.** The
   feed first used `store.Revision` as its revision; switching Mountains ↔
   Cities (both 9 rows, same count, same revision) served stale batches while
   every count-changing filter worked. The fix is a local revision bumped on
   every filter mutation plus every store notification — the same discipline
   TaskBoard uses. This failure mode (equal-count staleness) deserves an
   explicit sentence in the List docs.
8. **Slider needs an initial value on every render.** The goal slider passes
   `value: store.GoalKm` in per-render options (consumed only at creation) and
   re-seats from the `Reset` event. A retained-control "rebind on model jump"
   story is missing; the detail-view effect-sync pattern does not transfer
   because the goal has no selection change to key off.
9. **Row button ids repeat by design.** Every feed row declares `Button("like")`
   and relies on event tokens, not ids, for routing (same as the gallery's
   `activity-row`). This works but deserves one explicit sentence in the row
   restrictions: ids scope to the row root, payloads carry identity.

## Documentation nits found

- A `Div` wrapper without an explicit height breaks the fill chain exactly like
  an unsized overlay trigger: intrinsic-height siblings (toolbar, rail, chips)
  render while the virtual list's `Grow` collapses to a zero viewport and no
  rows materialize. Worse, `Grow` is inert inside a block parent at all — the
  wrapper itself must be a flex container (`VStack`/`HStack`), not merely sized.
  The rule "every link between a definite height and a virtual collection must
  be a sized flex ancestor, down to the collection" belongs next to the
  overlay-trigger note in `COMPONENTS.md`.
- GPUI012's message states the no-props arity; for props-bearing Views it
  should name the `(int, in TProps, ref RenderContext)` shape (the lifecycle
  doc has it, the diagnostic does not).
- The `Sheet(side)` option reads like free placement, but all sides are
  window-edge anchored; container-anchored sheets (item 2) are a real
  limitation worth stating in `COMPONENTS.md`.
