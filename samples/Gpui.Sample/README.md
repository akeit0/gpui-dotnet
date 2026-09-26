# GPUI.NET sample

Run with `dotnet run --project samples/Gpui.Sample` from the repository root. The **Overlays**
page includes window-owned toast controls. Show and Replace use the same ID to demonstrate
replacement; Dismiss removes that ID, while Clear removes every toast in the window. A toast
times out after five seconds by default and pauses while its stack is hovered.

The **Windows** page can open companion windows with normal, maximized, or fullscreen initial
state. Their configured size is used when the window leaves maximized or fullscreen mode.
The minimum-size button opens a window with a native 560 × 380 minimum-size request.

Run with `dotnet run --project samples/Gpui.Sample -- --persist-window` to save the primary
window's final placement to
`Gpui.Sample/window-placement.json` under the current user's local application-data directory.
The next run with that flag restores its normal bounds and maximized or fullscreen state. The
sample owns this JSON policy; the core library reports the native placement when the window closes.
The file is written from the primary window's `Closed` event, even while a companion remains open.
Some window managers may ignore requested absolute positions.
