# GPUI.NET sample

Run with `dotnet run --project samples/Gpui.Sample` from the repository root. The **Overlays**
page includes window-owned toast controls. Show and Replace use the same ID to demonstrate
replacement; Dismiss removes that ID, while Clear removes every toast in the window. A toast
times out after five seconds by default and pauses while its stack is hovered.
