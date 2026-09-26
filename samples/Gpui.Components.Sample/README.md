# Component extension sample

Run from the repository root:

```sh
dotnet run --project samples/Gpui.Components.Sample/Gpui.Components.Sample.csproj
```

The sample loads the optional component host, which includes the gpui-kit catalog and Editor. It
demonstrates display elements, controlled inputs, file presentation, a conversation, and retained
editing. The Review form batches a core Subject input, optional Ready checkbox, and a full-width
multiline Notes field with a shared footer. Clear notes shows the field error; typing clears it.
The Select below the form searches review states and can clear the selected ID. The Combobox filters
topics and keeps a selected ID set while its popup is open. Both include a disabled choice. The
sample updates managed selection from one event per interaction; native state handles popup,
keyboard, search, and scrolling without per-item callbacks.
The file Tree below the selection controls starts with two expanded folders. Click a row to request
selection, use Up/Down to move the native cursor, Left/Right to collapse or expand, and Space to
request the cursor's ID. Expansion persists when unrelated sample state changes. The disabled
README row cannot be activated.
The Accordion below the Tree starts with Review guidance open. Reorder sections to see the open
section follow its ID, switch between single and multiple open modes, and try the disabled
Archived policy header. Click the button inside the Review guidance panel to check that panel
content does not toggle its header. With keyboard focus on the group, Up/Down or Home/End chooses
an enabled header and Enter/Space requests its next open state.
The notes field retains text, selection, and undo natively. Focus notes sends a focus command;
Clear notes replaces the value and updates the sample's character count. The empty
state uses the sample's `Assets/archive-box.svg` file through the native Icon
component's application asset path. Its two-line description exercises generated multiline text
configuration. The Save button uses an icon from the host's bundled assets.
The file details use a native DescriptionList with managed text, a Tag value, a separator, and a
two-column entry. Archive and status changes update those details.
The breadcrumb at the top batches three path items; clicking Catalog or Files reports its stable
item ID through one managed callback. The current file item is disabled.
The tab bar below it uses a controlled selected ID. Click Overview, Activity, or Settings to
replace the panel from managed state. The native component presents the selected tab and can
show an overflow menu when the window narrows.
Press Tab to focus the strip, then Left/Right or Home/End to switch panels; Enter and Space
activate the current tab. Disabled tabs are skipped.

To try the file states, find `release-notes.pdf` in the broader catalog. `Finish` changes its
status to complete, and `Restart` returns it to uploading. `Archive` moves the card into the
archived section and replaces the empty state; `Restore` moves it back.
