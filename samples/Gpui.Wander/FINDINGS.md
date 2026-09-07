# Wander findings — mobile-style gaps

Built while writing `samples/Gpui.Wander`. Complements `TaskBoard/FINDINGS.md`;
items here are specific to web/mobile-style composition.

## What worked well

- A phone column + bottom tab bar composes entirely from `Div`/`HStack`/`VStack`,
  buttons, badges, and one keyed child slot. No tab primitive was missed.
- Bottom `Sheet` detail panels feel native with zero animation code.
- Store notifications keep feed projections and shell badges consistent after likes.
  Targeted refresh requires an up-to-date row source; it cannot repair stale records.
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
6. **Typed collection changes need a reusable contract.** Likes currently bump the
   store revision and notify all consumers. A finer-grained implementation must update
   immutable projections before refreshing intersecting native batches and notify other
   consumers separately. `RefreshRanges` already invalidates its owning View; it does not
   notify unrelated Views. A three-call wrapper alone is insufficient.
7. **`contentRevision` must cover filter state, not just store mutations.** The
   feed first used `store.Revision` as its revision; switching Mountains ↔
   Cities (both 9 rows, same count, same revision) served stale batches while
   every count-changing filter worked. The fix is a local revision bumped on
   every filter mutation plus every store notification — the same discipline
   TaskBoard uses. This failure mode (equal-count staleness) deserves an
   explicit sentence in the List docs.
8. **Model reset is different from initial configuration.** Slider options seed a
   retained resource only at creation. Profile uses an accepted effect keyed by store
   identity, `ResetRevision`, and field snapshots. Reset discards drafts; ordinary external
   changes replace only changed fields, including the goal slider. Save reconciles pending
   external changes before committing. Same-field replacement can cancel IME composition;
   a general binding should expose selectable conflict and composition policies.
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
- Props-bearing row diagnostics must name the `(int, in TProps, ref RenderContext)`
  shape rather than the no-props overload.
- The `Sheet(side)` option reads like free placement, but all sides are
  window-edge anchored; container-anchored sheets (item 2) are a real
  limitation worth stating in `COMPONENTS.md`.
