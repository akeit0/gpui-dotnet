# MoonBit C-backend frontend

## Boundary and scope

`moonbit/` is a second semantic frontend over the existing native ABI. It does not load the C#
assemblies or embed a CLR. The Rust implementation, `ABI_VERSION = 8`, and base semantic schema are
unchanged. MoonBit replaces application state, element construction, and callback ownership.

```text
bindings/schema.json          crates/gpui-dotnet/src/abi.rs
         |                                  |
 .NET semantic generator              gpui-abi-gen (syn)
         |                                  |
 semantic.g.mbt + elements.g.mbt       gpui_native.g.h
         |                                  |
 MoonBit application/runtime --> C bridge --> existing native host --> GPUI
         |                         |
    closure registries        C-owned RenderArena buffers
```

The ordinary application API is closure-first. It deliberately does not reproduce C# View
inheritance, source-generated factories, Signals, effects, or a managed Task scheduler. Ordinary
`Ref`/model state plus explicit invalidation is sufficient for the initial frontend. A future
reactive layer should sit above this runtime, not change the native wire protocol.

| Surface | Implemented boundary |
| --- | --- |
| App/windows | One native application loop, multiple windows, startup/close/dynamic callbacks, title/resize/close commands |
| Elements | Frame-owned builders; generated semantic operations; scalar/data/capability checks and native validation |
| Events | Capturing closures, typed click/input/shortcut helpers, general copied control events |
| Virtual lists | Batched range closures, stable datasource keys, content revision, artifact accept/release, invalidation |
| Retained controls | Input/slider/scroll factories and basic commands; generic resource-command path |
| Application menus | Hierarchical menu/action/separator declarations with acknowledged callback generations |
| Extensions | Version/hash capability query, configuration envelope, event tokens and command transport; editor schema metadata |
| Other native semantics | Generic nodes, generated operations and byte-payload commands, not complete high-level codecs |

This is not a claim of full C# API parity. In particular, Table datasource authoring, typed Dock
layout persistence, image/vector/theme payload builders, and a typed editor widget still require
component-specific MoonBit codecs/convenience APIs. There is no public async worker ingress,
hot reload, runtime restart, or library hot-unload implementation. Do not infer those capabilities
from the presence of generated constants or operations.

### Known limitation: full root re-render

There is no retained View tree. Each window owns one render closure, and any
`ctx.invalidate()` / `Window::invalidate()` re-runs that window's whole root render: a small
change in one window costs a full render of that window. Native list batch reuse
(`content_revision`, `ListController` refresh/splice/reset) and retained control state reduce
native revalidation work, but the MoonBit render closure itself always runs end to end.
Invalidation is scoped per window session only, so partition independent content across
windows when full-root cost matters. Per-subtree dirty tracking needs retained component
identity plus an incremental reconcile protocol; that is open design work, not a helper addition.

## Toolchain and setup

Use a MoonBit toolchain supporting the documented C FFI (`#external`, `FuncRef`, `#borrow`,
immutable `Bytes`, and `FixedArray[Byte]`), .NET SDK 10 for semantic generation, stable Rust/Cargo for
ABI generation and the native host, Python 3.10+, and a C11 compiler. The implementation follows
the MoonBit v0.10.12 documentation; it is not yet a compiler-version compatibility certification.
Record `moon version --all`, `dotnet --version`, `rustc --version`, and your C compiler version
when reporting build failures. The standalone ABI generator has its own lockfile/workspace.

On Windows use `cl.exe` or `clang-cl.exe` in an x64 developer shell, with the Windows SDK and C++
tools installed. MinGW is not a supported MoonBit native compiler. The sample C stub requests the
Common Controls v6 manifest; a separate consumer executable must provide its own manifest.
On macOS use the normal Apple development tools. Linux requires the native GPUI desktop packages
described in the repository's native setup.

The source archive may not contain `external/gpui-kit`. In a normal Git checkout, restore the
recorded submodule revisions before building the native host:

```sh
git submodule update --init --recursive
```

That command cannot reconstruct missing Git metadata from a source ZIP. For an archive-only
checkout, restore the matching submodule contents from your original checkout; do not substitute
an arbitrary current GPUI revision. Generation and C/logic tests do not need the submodule.

### Generation and non-GUI checks

Run from the repository root:

```sh
python tools/moonbit.py static-test
python tools/moonbit.py generator-test
python tools/moonbit.py abi-test
python tools/moonbit.py verify
python tools/moonbit.py c-test
python tools/moonbit.py c-test --cc clang --sanitize
python tools/moonbit.py check
python tools/moonbit.py test
python tools/moonbit.py build
```

`static-test` checks JSON, generated-symbol references, borrows, schema consistency, sample method
names, and safe staging behavior. It does not parse/type-check MoonBit or compile either generator.
`generator-test` compiles the .NET generator alone and uses temporary fixture roots; it verifies
all committed outputs, including the pre-existing C#/Rust files. `abi-test` compiles the independent
Rust header generator. `verify` invokes both authoritative generators without changing outputs.
Do not repair a verification failure by hand-editing a generated file. Change its emitter or source
schema and run `python tools/moonbit.py generate`, then inspect the diff.

`c-test` compiles a deterministic native ABI peer and the real bridge without GPUI, a MoonBit
runtime, or a window server. It exercises all twelve callbacks, native commands, schema mismatch,
truncated/null API tables, delayed publication access, accept/release, duplicate/mismatched
acknowledgements, output rollback, thread/reentry rejection, and malformed packets. Sanitizers are
available on Linux/macOS through the helper; the normal C test also has an MSVC command path.

`test` runs `runtime_wbtest.mbt`: frame sealing, cross-frame/capability checks, transaction commit,
fault handling, independent artifact lifetimes, callback expiry, token retirement, cleanup ordering,
and menu generation retirement. These tests do not launch GPUI. Passing C tests does not establish
that MoonBit source compiles or that a real GPUI host runs; all layers must be checked separately.

The helper copies `moonbit/` to a marked, disposable `artifacts/moonbit-stage` directory and sets
platform `link.native` options there. It refuses to delete an unmarked directory. `MOONBIT_NEW_NATIVE=0`
is set for every `moon` invocation. Linux adds `-ldl` for both ordinary and separate stub linking.
The tracked package JSON stays platform-neutral. `--cc` sets `cc` and `stub-cc` together. Each helper
invocation refreshes the staging copy; edit the original module, not build outputs.

For manual development, `python tools/moonbit.py stage --cc clang` creates the same copy without
calling MoonBit. Change into that directory, export `MOONBIT_NEW_NATIVE=0`, and run `moon check
--target native`, `moon test --target native`, or `moon build --target native`. Do not drop the C
backend selection merely because the target is named `native`.

### Native host and sample

Use the existing `eng/build-native.ps1` workflow or build the actual default-host crate after
restoring platform dependencies/submodules:

```sh
cargo build --manifest-path crates/gpui-dotnet/Cargo.toml -p gpui-dotnet-default-host --release
```

The workspace core crate is an `rlib`; link/load the **default host's cdylib**, not the core crate.
Depending on platform it is `libgpui_dotnet.dylib`, `libgpui_dotnet.so`, or `gpui_dotnet.dll`.
Cargo target-directory configuration may change its exact location. Pass the real path explicitly:

```sh
python tools/moonbit.py run --library /absolute/path/to/libgpui_dotnet.dylib
```

`run` is the only helper command that opens a GUI. It resolves the library path before changing
directories, exports `GPUI_MOON_NATIVE`, and runs `examples/counter`. No DLL/library is downloaded or
searched by a guessed package version. Code-loading trusts the supplied library; a C ABI is not a
sandbox for untrusted native code. macOS library signing and dependent-library resolution remain
normal deployment responsibilities.

## Application API

```moonbit
fn launch(path : String) -> Result[Unit, @gpui.UiError] {
  let app = @gpui.App::new()
  let count : Ref[Int] = { val: 0 }
  match app.window("MoonBit + GPUI", fn(ui) {
    ui.div().v_stack().gap_px(12.0).padding_px(24.0)
      .child(ui.text("Count: \{count.val}"))
      .child(ui.button("increment", "+1").on_click(fn(ctx, _) {
        count.val = count.val + 1
        ctx.invalidate()
      }))
  }) {
    Err(error) => return Err(error)
    Ok(_) => ()
  }
  app.run(path)
}
```

Import `akeit0/gpui` in the consumer package. This example is an application function, not a
complete entry point; the runnable sample supplies environment-path handling and process status.
The library is repository-local and is not assumed to be published to Mooncakes.

`App::window(title, render, width=..., height=..., title_bar=..., on_closed=..., on_frame=...)`
declares a root before `run` or opens one from startup/an event. `title_bar=0` uses the native system
mode, `1` requests custom content that the application must compose, and `2` hides the title bar.
The frontend does not synthesize the C# title-bar/menu UI.

Declare state outside `render`. Rendering may allocate local scratch, declare event bindings, and
construct a frame, but must not mutate observable application state, perform I/O, schedule work,
or call native commands. Native-command entry points reject calls during root and range rendering.
This guard cannot prevent arbitrary user mutation of a captured `Ref`; purity remains an API rule.

`Frame`/`Element` are abstract handles. Elements cannot move between frames, have multiple parents,
or be mutated after publication. A completed frame must be a connected, single-root tree with depth
at most 128. Raw data is copied. `Frame::fail` records an explicit declaration error without raising.
The generic `node_bytes`, scalar/data operations, and `on` are escape hatches within semantic checks,
not permission to bypass the ownership protocol. Owner identifiers are runtime-reserved to root 1.

Use generated fluent methods for ordinary styling. The schema supplies all 279 operation identities;
five ownership/callback-sensitive operations use handwritten handling instead of ordinary generated
methods. Some complex `data` operations deliberately accept `Bytes`; those bytes must follow the
native component codec. A generated method alone does not implement that codec.

## Callbacks and errors

Application callbacks may capture ordinary MoonBit state. Only the closed `dispatch_callback`
function crosses the C boundary as a `FuncRef`; C never receives a MoonBit closure, object header,
GC handle, or runtime-owned array to retain. `running_app` roots the application for the native run,
and sessions/publications retain the appropriate closures. Stable integer tokens, not addresses,
are written into render records.

`on_click` delivers `ClickEvent { x, y, buttons, modifiers, payload }`.
`on_input_changed` delivers a decoded string and native revision. Generated callback methods accept
`(EventContext, Event) -> Unit`, where `Event` is `Click(...)` or `Control(...)`.
`ControlEvent` contains `kind`, `flags`, `revision`, and an immutable copy of the native data bytes;
`read_u64(offset)` validates bounds. Decode binary event payloads according to [ABI.md](ABI.md).
`on_shortcut(packed_shortcut, handler)` receives a control-event notification and invokes
`handler(EventContext)`; it is not a mouse click.

`EventContext` is valid only during its callback. `ctx.invalidate()`, `ctx.close()`, `ctx.input_focus`,
`ctx.input_set_value`, `ctx.input_set_if_current`, `ctx.slider_set_value`, and `ctx.scroll_to_top`
record errors in the delivery scope. `ctx.check(result)` handles explicit `Result[Unit, UiError]`
commands. `ctx.window()` returns the stable window handle, but retaining the context itself does
not extend callback permissions. Window methods return errors after close has been requested or
the route retired. Queued native work is still serviced until `window_closed`; close is not a
synchronous revocation of already-scheduled native callbacks.

For startup/menu callbacks, use `app.check(result)` to retain explicit command failures.
`Frame::fail`, `EventContext::fail`, and `App::error` use ordinary error values; `App::run` returns
the first recorded failure. Callbacks are non-raising function types. MoonBit aborts, out-of-memory
failures, or native process faults are not converted into recoverable statuses. Do not assume an
exception handler at this ABI boundary.

All callbacks and native mutations must run on the OS application/main thread. The bridge rejects
foreign-thread entry before invoking MoonBit, and rejects same-thread nested callback dispatch
before mutating publication ownership. This is not a scheduler: worker results cannot call these
APIs directly. `App::run` must be invoked on the OS main thread; a thread-local guard cannot prove
that the thread initially invoking it is the platform's required main thread.

## Publication and memory ownership

The bridge-facing packet is little-endian and versioned separately from the native C ABI. Its
32-byte header holds magic, node/operation/edge/data lengths and the root; the final eight bytes are
reserved zero. Node rows are 12 bytes, operations 24 bytes, and edges 8 bytes. The C bridge validates
length arithmetic, reserved fields, counts, and root bounds, then decodes aligned C-owned arrays.
Rust still performs the authoritative semantic/tree/component validation.

| Lifecycle | Buffer ownership | MoonBit closure ownership |
| --- | --- | --- |
| Root callback returns successfully | C retains decoded arena | Candidate event/range maps stay pending |
| Matching `render_completed` succeeds | C frees arena | Candidate maps replace accepted root maps |
| Root rejected / callback fails | C rolls back or frees on acknowledgement | Candidate bindings retire; accepted maps are not installed over on failure |
| Range callback returns successfully | C retains arena by session/source/artifact | Item callbacks are pending |
| `accept_artifact` | C frees arena | Item callbacks become live |
| `release_artifact` | Any unaccepted arena is freed | Item callback registry is removed independently of the current root |
| Window close / run return | Remaining buffers are freed | Routes are revoked and closure cycles broken before user cleanup |

Never free a successful render's arena merely because the render callback returned: the native
host can still borrow it. Conversely, acceptance of an item artifact ends its buffer lifetime but
does not end its event lifetime. Root replacement must not discard callbacks retained by native
virtual-item artifacts. Release is idempotent for already-retired artifacts and legal during a
pending root or fault. Mismatched identities do not free a different publication.

There is one pending root publication per session. Event/range dispatch during that transaction is
rejected. Root/range/event identities are monotonic and never recycled within the session.
Retained resource identity is `(session, owner=1, UTF-8 key)`. Keys must be nonempty, at most 4096
UTF-8 bytes, and contain no control byte below 0x20. Removed list keys are pruned after acceptance;
reintroducing a removed key allocates a fresh datasource token.

The frontend limits frame payload bytes to 64 MiB and each record family to 1,048,576 rows. The C
packet limit is 256 MiB. Range demand is at most 512 items and must fit the declared list bounds.
These are defensive ceilings, not performance targets. Avoid reaching them in routine rendering.

## Virtual lists

`ui.list(key, count, content_revision, fn(items, start, length) { ... })` declares a batched source.
Return exactly `length` item roots created from the supplied `items` frame. The runtime creates
the wrapper root expected by the native ABI. Do not call the foreign function interface per row.

Item renderers produce element-only snapshots: no nested lists, inputs, sliders, dock resources,
native extensions, dynamic nodes, or deferred layers. Item click handlers are supported. Capture
the accepted render's immutable model/snapshot values when constructing the range closure.
Increase `content_revision` when item data changes; update captured application state in an event
and invalidate the root. `window.invalidate_items()` explicitly invalidates accepted artifacts.

For scoped batch invalidation without changing count, use `window.list_controller(key)`:
`refresh(start, count)` / `refresh_ranges(ranges)` discard only intersecting batches and
measurements, `splice(start, removed, inserted)` preserves unaffected measurements across
structural edits, `reset(count)` covers arbitrary reorder, and `scroll_to_item(index)` moves
without content change. Queue the command, then invalidate the root; native commits the queued
sequence in one pass. This mirrors the C# `ListController` resource-command packing. There is
no per-View subtree dirty tracking: `ctx.invalidate()` always re-runs the whole root render,
so partition independent content across windows when full-root cost matters. Invalidation is
per-window session: `ctx.invalidate()` re-renders only the event's window, and any retained
`Window` handle can be invalidated directly (see the counter sample's cross-window bump).

## Menus and extensions

`app.set_menu([Menu(title, children), Action(title, callback), Separator, ...])` uses hierarchical
declarations. Only menus are legal at the root. Action IDs are non-reused, and each publication has
a generation. A native `menu_applied` acknowledgement retires only older generations, preserving
newer pending menus; callbacks from potentially native-referenced pending generations remain
rooted. The implementation bounds menu size/depth and outstanding generations.

`Extension { id, component, version, schema_hash }` plus `Frame::extension` builds the native
six-field UTF-8 envelope. `Frame::bind_extension_event` allocates a frame-owned token for an
extension-specific configuration codec. `Window::extension_command` transports a copied payload
and optional revision/flags. `supports_extension` returns `Ok(true)` only for native status 0,
`Ok(false)` for provider-not-installed (-81), and an error for schema mismatch (-82) or malformed
queries. Never interpret a hash mismatch as a harmless absent optional feature.

The editor metadata package is `akeit0/gpui/extensions/editor`; its constants must be paired with
an editor-enabled native host. The default host has no editor provider. The core library does not
pretend that editor JSON/configuration fields can be derived from ABI scalar types alone.

## Generation and maintenance policy

Three sources have distinct owners: `bindings/schema.json` for shared semantics,
`crates/gpui-dotnet/src/abi.rs` for native layouts, and `bindings/moonbit/bridge.json` for the private
flat bridge. Independent extension schemas keep independent hashes. The same .NET semantic
canonicalization/hash computation is used for all frontends; adding MoonBit does not fork IDs.

`moon-bindgen` is a design reference, **not a mandatory dependency of this implementation**. Its
generated-FFI and layout-shim approach is useful, but generating direct imports alone does not
solve an API table, asynchronous arena borrowing, or artifact-owned callback registries. A small
schema-based bridge generator and fail-closed Rust AST header generator keep those boundaries
explicit and avoid bringing MoonBit representation assumptions into native code.

The application process is one-shot. A failed library load can be retried before entering the
native loop; after entry, a second run is rejected. The bridge releases its own allocations but
keeps the loaded native code image mapped until process exit, because GPUI/Rust may retain global
registrations or worker code. Do not implement unload by calling `dlclose`/`FreeLibrary` after run
without a separate, proven native shutdown contract.

When extending this frontend, add schema generation tests and MoonBit lifetime tests together.
Do not change the Rust ABI merely to make one language's struct layout convenient. Keep high-
frequency pointer, IME, scroll, measurement, and retained control state native. Add bulk codecs
before adding per-property foreign calls. A distributable package should separately pin a tested
MoonBit compiler, run end-to-end native tests on each supported platform, and define binary asset
packaging; this repository-local frontend does not claim those release guarantees yet.

## Primary references

- [MoonBit FFI and backend selection](https://docs.moonbitlang.com/en/latest/language/ffi.html)
- [MoonBit package/native compiler configuration](https://docs.moonbitlang.com/en/latest/toolchain/moon/package.html)
- [MoonBit FFI source documentation](https://github.com/moonbitlang/moonbit-docs/blob/main/next/language/ffi.md)
- [moon-bindgen](https://github.com/nuskey8/moon-bindgen)
- Repository [native ABI](ABI.md), [binding generation](BINDING_GENERATION.md), and [collections](COLLECTIONS.md)
