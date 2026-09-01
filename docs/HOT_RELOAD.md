# Managed renderer Hot Reload

> Status: implemented for the standard CLR metadata-update path.

GPUI.NET supports the standard .NET Hot Reload workflow for application-side managed code:

```sh
dotnet watch --project path/to/App.csproj
```

The binding reacts to CLR metadata updates and renders the existing managed View tree again. It
does not load replacement assemblies, recreate the application, or introduce a second dynamic
component model.

## Goals

- Show supported edits to `Render()`, list/table row renderers, event handlers, and style helpers
  without restarting the process.
- Preserve existing View instances and their fields, committed props, controllers, and lifetime.
- Preserve native input, focus, scrolling, selection, window, and retained-resource state when
  stable resource keys still match.
- Refresh all managed fragments and every native row snapshot that can contain managed output.
- Remain cross-platform and use the same path on macOS and Windows.
- Keep all Hot Reload work outside normal frame and event hot paths.

## Non-goals

- Reloading Rust code, the native library, the semantic schema, or the C ABI.
- Supporting NativeAOT, trimming-oriented Release builds, or arbitrary runtime assembly plugins.
- Re-running constructors or `OnMounted()` for already mounted Views.
- Migrating state across a changed View base type, props type, or generic shape.
- Watching JSON settings, image files, or other content files. Those need separate content-reload
  policies.

## Why metadata updates fit the renderer

The CLR applies supported edits to code in the existing process. GPUI.NET already calls virtual
managed methods whenever a fragment or virtual row must be rendered, so the next invocation can
execute updated IL on the existing object.

The retained renderer currently prevents that invocation when nothing else is dirty. It also keeps
native List and Table row batches after unrelated managed renders when their content revision is
stable. Hot Reload therefore needs an explicit invalidation path on both sides of the binding.

Replacing assemblies through `AssemblyLoadContext` is the wrong model. Existing Views, delegates,
props, generated generic factories, and controllers would retain the old type identities and make
safe unloading or state migration impractical.

## Runtime flow

```text
Roslyn emits a supported metadata delta
                 │
                 ▼
CLR updates methods and metadata in place
                 │
                 ▼
GPUI.NET metadata-update handler
                 │
                 ├── mark all managed View fragments dirty
                 │
                 └── enqueue ManagedCodeUpdated to the native application
                                      │
                                      ▼
                    clear managed List/Table row snapshots
                    and invalidate each native ManagedView
                                      │
                                      ▼
                         next GPUI frame calls updated C#
```

The managed dirty state must be established before the native command is enqueued. When the native
application processes the command and requests a frame, the following render is guaranteed to see
the new required versions. An update racing an existing render may allow that render to finish, but
the queued invalidation must produce another render afterward.

## Metadata-update entry point

`Gpui.Core` registers one internal assembly-level `MetadataUpdateHandler` with this entry point:

```csharp
static void UpdateApplication(Type[]? updatedTypes)
```

There is currently no metadata-derived reflection cache that requires `ClearCache`. Event binder
entries and generated factories are based on stable runtime identities and must not be cleared.

The handler invalidates every active GPUI application. It does not filter to Views whose
runtime type appears in `updatedTypes`: a changed style helper, service, extension method, or theme
factory can affect any View. Metadata updates are rare development events, so correctness is more
important than selective invalidation.

The handler can run on a non-GPUI thread. It therefore:

- use only thread-safe application/session ingress;
- never call `Render()`, lifecycle hooks, event handlers, or controller methods directly;
- tolerate applications starting or stopping concurrently;
- isolate failures per application and never throw through the runtime's metadata-update callback.

There is no public Hot Reload API or application opt-in. The feature activates automatically when
the runtime applies an update.

The handler, registry walk, and native update command run only after a metadata update. Normal
render and event paths do not query `MetadataUpdater.IsSupported`, poll files, or allocate Hot
Reload state. Release rendering therefore has no per-frame Hot Reload branch or allocation.

## Managed invalidation

Each live `ManagedSession` increments `RequiredVersion` for every retained View fragment. The
existing full-fragment invalidation used by application-wide theme changes is the right primitive.

This preserves:

- the root and child View instances;
- committed props and local fields;
- child-slot identity;
- lazy lifetime tokens and mounted ownership;
- event-entry storage until the updated render declares the next binding pass.

The update must not remount the tree. `OnMounted()` and constructors describe one-shot lifetime and
are not refresh hooks. Changes to them apply only to subsequently created instances.

## Native invalidation

A distinct native application command, `ManagedCodeUpdated`:

1. invalidate every cached List and Table row batch;
2. mark every native `ManagedView` dirty;
3. refresh its window.

It preserves retained controls and interaction state. Inputs, scroll containers, sliders,
focus, overlays, and other resources will reconcile normally against the next snapshot. Resources
whose stable owner/key identity disappears from that snapshot will be released by ordinary
retention.

Do not reuse `SetTheme` as the Hot Reload command merely because it currently performs similar row
invalidation. Theme and managed-code revisions are separate causes and should remain independently
observable and testable.

## Event bindings

The full managed rerender refreshes event declarations. Compatible existing delegates generally
target updated method bodies immediately; bindings whose generated delegate or closure changes are
reconciled by the next event-binding pass.

An event can race the short interval between metadata application and the requested frame. It may
observe the last committed binding, but it must never observe a partially rendered binding table.
Normal render commit/error behavior remains authoritative.

## Virtual List and Table rows

Invalidating only managed fragments is insufficient. A List or Table with an explicit unchanged
content revision can continue displaying previously rendered native row snapshots indefinitely.
The native Hot Reload command must clear those batches even when item count, renderer token, and
content revision are unchanged. New rows remain lazily requested in coarse managed batches.

Scroll position, list interaction state, and measurements should be preserved unless the updated
declaration changes a structural option that already requires a normal reset.

## Generated View shape

Generated factory support and virtual-row dispatch have a stable initial shape. Every `[GpuiView]`
receives its factory, an empty `RenderListItem` override, and a `Rows` helper even when the View has
no `[GpuiListItem]` methods. Adding the first list renderer then updates existing dispatch machinery
instead of introducing the virtual override during Hot Reload. Renderer ids remain derived from
stable method identity; changing that identity is allowed to produce a new token because native row
batches are cleared for the update.

## Render failure recovery

Compilation failures do not apply a metadata delta, so the running application should continue to
show its last committed UI.

A compiling edit can still throw or produce invalid semantic output. Native has a managed render
error surface, and the managed session distinguishes between:

- recoverable render/list-render failures; and
- terminal event, lifecycle, interop, or native failures.

A later metadata update clears only recoverable renderer failures and requests a new render. A
successful render removes the native error surface. The update does not erase unrelated terminal
failures.

## Expected edit behavior

| Edit | Expected result |
| --- | --- |
| Change text, colors, spacing, or layout in `Render()` | Existing View rerenders |
| Change a style/helper method used by Views | All Views rerender |
| Change a compatible event-handler or lambda body | Updated behavior and refreshed bindings |
| Add/remove/reorder child declarations | Normal slot reconciliation and terminal unmount |
| Change an existing `[GpuiListItem]` body | Native batches clear and rows regenerate lazily |
| Add the first `[GpuiListItem]` | Supported after stable generated scaffolding |
| Add an ordinary field | Runtime-dependent; existing instances receive no constructor migration |
| Change constructor, initializer, or `OnMounted()` | Existing instances are not reinitialized |
| Change View base type, `TProps`, generic constraints, or incompatible signatures | Restart |
| Change generated/native binding code, Rust, schema, or ABI | Rebuild and restart |
| Change JSON settings or file assets | Outside metadata Hot Reload |

The authoritative supported-edit boundary belongs to the active .NET runtime and Roslyn version.
`dotnet watch` should restart the application for unsupported or rude edits rather than GPUI.NET
attempting its own fallback type migration.

## Verification

Automated managed tests verify handler registration and harmless updates when no application is
running. Generator tests verify that a View with no row renderer still receives the stable generated
row scaffold. Native tests verify command scoping and that ambient managed-render changes clear
retained List/Table row batches.

The macOS sample has also been exercised under `dotnet watch`: a method-body edit to the mounted root
View reached the metadata handler and immediately rendered the updated method without restarting the
process. Adding the first `[GpuiListItem]` to an existing View also applied in process.

Broader framework tests should continue to cover:

- every retained fragment becomes dirty;
- an update before first render is harmless;
- updates racing rendering or shutdown are coalesced safely;
- View instances, props, and mounted lifetime are preserved;
- event bindings are refreshed by the next successful render;
- recoverable renderer failures do not clear terminal failures.

Native coverage should continue to verify that `ManagedCodeUpdated`:

- clears List and Table row batches;
- dirties every managed native window;
- preserves input, scroll, focus, slider, and other retained-resource identities;
- is safe with zero windows and during normal window removal.

Release acceptance should run a static, non-animated sample through `dotnet watch` on macOS and
Windows. It should cover text/layout changes, event behavior, an unchanged-revision virtual list,
child replacement, a compiling render failure followed by recovery, rapid saves, and window-close
races. The static screen is important: an animation or user event must not accidentally provide the
invalidation that the Hot Reload integration is intended to prove.

## References

- [.NET `dotnet watch`](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-watch)
- [.NET `MetadataUpdateHandlerAttribute`](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.metadata.metadataupdatehandlerattribute?view=net-10.0)
- [Roslyn supported Hot Reload edits](https://github.com/dotnet/roslyn/blob/main/docs/wiki/EnC-Supported-Edits.md)
- [Uno Platform metadata-update handler](https://github.com/unoplatform/uno/blob/master/src/Uno.UI/RuntimeTypeMetadataUpdateHandler.cs)
