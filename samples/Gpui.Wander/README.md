# Wander — Travel Journal

A mobile-style GPUI.NET application sample: a travel journal rendered as a phone
column (status spoof, tab content, bottom tab bar) centered on the desktop window.
Where TaskBoard is a dense desktop console, Wander composes the same primitives
the way a mobile app would.

Run from the repository root:

```sh
dotnet run --project samples/Gpui.Wander
dotnet run --project samples/Gpui.Wander -- --dark
```

## Tour

- **Explore tab** — retained search `Input`, horizontal stories rail (retained
  `Scroll`), filter chips, and a virtual `List` feed. Likes notify the store's
  subscribers and rebuild the immutable-record projection through its revision.
  A separate projection revision resets native cursor/scroll state when visible row IDs change;
  likes retain the current position because they only change content.
  Tapping a place opens a bottom `Sheet` with a native-decoded SVG cover, like
  and add-to-trip actions. Refresh simulates pull-to-refresh through `WorkScope`.
- **Trips tab** — `Grid` of trip cards, bottom detail `Sheet` with a 7-day
  `Checkbox` plan and star-rating buttons, plus a centered new-trip `Dialog`.
- **Stats tab** — memo-cached season summary, week bars, an animated vector
  `Drawing` chart (`Dynamic` + replay button), and a goal progress bar.
- **Profile tab** — `Image` avatar, draft-then-save identity inputs, notification
  `Checkbox`, retained goal `Slider`, theme `Radio` group, demo reset.
- **Shell** — one keyed child slot shared by all tabs (switching replaces the
  child type), badge counts on tab buttons, Ctrl+1…4 hot keys, effect-installed
  menus, and `GpuiTitleBar.RenderWindow` for desktop chrome.

## Architecture notes

Button recipes declare complete normal/hover/pressed background and foreground pairs with
`InteractionColors`. Selected navigation and filter/rating chips use warm amber surfaces; liked
buttons use rose-pink surfaces and a filled heart. Unliked buttons use an outline heart. Selected
labels have a heavier weight, and tab icons, labels, and counts inherit their navigation foreground.
Both Wander themes keep button text readable across state backgrounds; badges use a matched
surface too. See [Styling](../../docs/STYLING.md).

Profile synchronizes model fields through an accepted effect. External changes to the same field
replace its local draft; unrelated field edits preserve drafts. Save rechecks the current model
before committing, and document reset discards all drafts. Input replacement is unconditional,
so same-field external edits can end active IME composition under this sample policy.

- `Models/TravelStore.cs` owns destinations, entries, trips, and profile state
  with a monotonic `Revision`. Observable mutations notify every consumer.
  `ResetRevision` separately identifies document resets that replace profile drafts.
- Props-bearing `[GpuiListItem]` methods take `(int index, in TProps props, ref
  RenderContext ui)` — rows read the store through accepted props, never through
  `CommittedProps`.
- Render code uses allocation-conscious mechanisms: ref-bound controllers, static
  handlers with `ulong` payloads (packed `tripId << 8 | day` where one payload
  must carry two values), stack-span collection expressions, one inline buffer
  for variable-length rails/cards, static id tables, and arena-direct
  interpolated text. This is not a zero-allocation measurement: projection rebuilds,
  capturing lookup predicates, and the current time-label helper can allocate.
  See `FINDINGS.md` for the gaps this surface exposed.
