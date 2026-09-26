# Component extension sample

Run from the repository root:

```sh
dotnet run --project samples/Gpui.Components.Sample/Gpui.Components.Sample.csproj
```

The sample loads the optional component host, which includes the gpui-kit catalog and Editor. It
demonstrates display elements, controlled inputs, file presentation, a conversation, and retained
editing. The empty state uses the sample's `Assets/archive-box.svg` file through the native Icon
component's application asset path. The Save button uses an icon from the host's bundled assets.
The file details use a native DescriptionList with managed text, a Tag value, a separator, and a
two-column entry. Archive and status changes update those details.
The breadcrumb at the top batches three path items; clicking Catalog or Files reports its stable
item ID through one managed callback. The current file item is disabled.

To try the file states, find `release-notes.pdf` in the broader catalog. `Finish` changes its
status to complete, and `Restart` returns it to uploading. `Archive` moves the card into the
archived section and replaces the empty state; `Restore` moves it back.
