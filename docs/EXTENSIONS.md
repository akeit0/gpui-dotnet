# Extensions and custom native hosts

Extensions keep optional component families out of the default managed API and native package.
The contract deliberately separates an extension's managed schema from its Rust runtime.

## Boundary

`GPUI.NET.Core` contains a generic `NativeExtension` semantic envelope, command/event transports,
and host negotiation. It does not contain extension-specific component kinds, configuration
fields, typed controllers or events, or native implementations.

An extension consists of two independently packaged halves:

```text
extension schema assembly
  typed C# builders + options + schema ID/version/hash

custom native host
  gpui-dotnet runtime + selected Rust providers, linked into one binary
```

The schema assembly references `GPUI.NET.Core`. It wraps `RenderContext.NativeExtension` with a
typed API and writes an opaque UTF-8 configuration owned by that extension schema. Extension
definitions do not enter `bindings/schema.json` and do not change the base semantic schema hash.

`bindings/extensions.json` registers extension schema files and their generated C#/Rust outputs.
The normal binding generator canonicalizes each schema, derives its independent hash, and emits the
shared identity, component-kind, flag, command, and event constants. Hand-maintained protocol
numbers are not part of an extension implementation.

Rust providers implement `gpui_dotnet::extension::NativeExtension`. A custom host calls
`install_native_extensions` once and delegates its `gpui_dotnet_get_api` export to
`gpui_dotnet::api`. The runtime crate is an `rlib`; explicit default and custom `cdylib` host crates
own the native entry-point exports. GPUI and Rust values never cross a dynamic-library boundary.

Runtime loading arbitrary Rust plugin DLLs is intentionally unsupported. Rust has no stable ABI,
and separately linked GPUI revisions would create incompatible type universes. Combining multiple
native extensions requires building one host with all selected providers.

## Compatibility

Every extension has:

- a stable ASCII identifier;
- an independent protocol version;
- a deterministic 64-bit schema hash;
- one or more component-kind identifiers.

`NativeRuntimeOptions.Extensions` lists the schemas required by an application. ABI version 3's
`supports_extension` entry verifies every ID/version/hash before the event loop starts. An extension
node repeats that identity in its envelope, so a declaration cannot accidentally reach a provider
built from another schema.

The retained resource identity is `(session, owner View, extension ID, component kind, key,
version, schema hash)`. Extension state lives in a type-erased store owned by the managed View's
native resource store and is dropped when the committed snapshot stops declaring it.

Typed schema packages wrap `NativeExtensionController`, normally through a factory on
`ViewContext`. The controller uses the stable any-thread View route and sends schema-owned command
IDs and opaque byte payloads through the generic ABI. A custom provider validates each command,
while Core queues copied payloads by the full resource identity until native materialization.
Typed schema packages also bind render-scoped callbacks through `NativeExtensionEventBinding` and
decode copied `NativeExtensionEvent` packets into their public event types. Event IDs, flags,
revisions, and payload layouts remain schema-owned.

## Optional editor probe

`src/Gpui.Editor` is a separate managed schema project. The
`gpui-dotnet-editor-host` crate is a separate custom host that registers a retained
`gpui-component` Editor provider. Neither project is referenced by the `GPUI.NET` or
`GPUI.NET.Core` package graph.

The sample proves build-time composition and startup negotiation:

```sh
dotnet run --project samples/Gpui.Editor.Sample/Gpui.Editor.Sample.csproj
```

Its project builds the custom host, copies the uniquely named native library beside the executable,
and selects it explicitly:

```csharp
var application = new GpuiApplication(
    new NativeRuntimeOptions
    {
        LibraryPath = Path.Combine(AppContext.BaseDirectory, "gpui_dotnet_editor.dll"),
        Extensions = [EditorExtension.Requirement],
    }
);
```

The editor probe retains native Rope, incremental Tree-sitter parse state, selection, scrolling,
highlighting, undo, focus, and IME state. Its custom host currently bundles only the Rust grammar;
unknown language identifiers render as plain text. Its managed schema exposes language,
disabled/read-only state, line numbers, optional fixed line-number width, folding, and whitespace
visibility. `EditorController.Bootstrap` transfers the initial UTF-8 document once, outside render
snapshots. Typed commands cover focus and
revision-checked selection, whole-document replacement, and one contiguous edit. Opt-in callbacks
report native edits as minimal contiguous UTF-8 replacements and report stale or invalid-range
commands explicitly. Release packaging remains open work.

The accepted ownership, revision, bootstrap, command, and event design is documented in
[EDITOR.md](EDITOR.md).

## Packaging guidance

- keep schema assemblies free of native assets;
- keep each host's Rust dependency and feature graph explicit; an optional schema assembly does not
  provide isolation if its provider crate is still linked by the default host;
- link provider-only component families, parsers, grammars, and assets only into the custom hosts
  that select them;
- avoid depending on a monolithic component façade for one provider when a feature-gated or
  narrowly scoped runtime crate can preserve the same contract;
- use a unique native host file name so custom and default hosts can coexist;
- put release host libraries under RID-specific runtime packages;
- build every host on its natural target platform;
- version each schema and its provider together;
- test missing-extension and schema-mismatch rejection before application startup;
- test a clean consumer restore without requiring Cargo or a Rust toolchain;
- record release artifact sizes and check that optional providers do not enter the default host.
