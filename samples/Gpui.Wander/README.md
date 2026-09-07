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
  `Scroll`), filter chips, and a virtual `List` feed. Likes write silently to the
  store and refresh only their row range; structural changes bump the revision.
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

- `Models/TravelStore.cs` owns destinations, entries, trips, and profile state
  with a monotonic `Revision`. `ToggleEntryLike` is deliberately silent (no bump,
  no notify) so the feed can use targeted `RefreshRanges`; everything structural
  notifies and bumps.
- Props-bearing `[GpuiListItem]` methods take `(int index, in TProps props, ref
  RenderContext ui)` — rows read the store through accepted props, never through
  `CommittedProps`.
- `Render` methods allocate nothing on the heap: ref-bound controllers, static
  handlers with `ulong` payloads (packed `tripId << 8 | day` where one payload
  must carry two values), stack-span collection expressions, one inline buffer
  for variable-length rails/cards, static id tables, and arena-direct
  interpolated text. See `FINDINGS.md` for the gaps this surface exposed.
