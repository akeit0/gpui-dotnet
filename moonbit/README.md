# GPUI for MoonBit

An experimental semantic MoonBit frontend for the existing GPUI.NET Rust native host. Application
state and event closures live in MoonBit; windows, retained controls, validation, and virtualized
materialization stay in Rust. Running an application does not require the .NET runtime.

This module targets the **MoonBit C backend**, not Wasm, JavaScript, or the experimental LLVM backend.
The development helper explicitly sets `MOONBIT_NEW_NATIVE=0`. Desktop processes are 64-bit.

## Start from the repository root

```sh
python tools/moonbit.py static-test
python tools/moonbit.py c-test
python tools/moonbit.py verify
python tools/moonbit.py check
python tools/moonbit.py test
python tools/moonbit.py build
```

`verify` needs .NET SDK 10 and Cargo; `check`/`test`/`build` need MoonBit and the platform C compiler.
None of those commands opens a window. Generated bindings are committed, so compiling the MoonBit
frontend does not require a native Rust build. A native host is needed only when running the sample.

Build the existing native default host using the repository native setup, then:

```sh
python tools/moonbit.py run --library /absolute/path/to/libgpui_dotnet.dylib
```

Use `libgpui_dotnet.so` on Linux and `gpui_dotnet.dll` on Windows. Windows requires an x64
MSVC-compatible developer shell. The helper stages a copy under `artifacts/moonbit-stage` and adds
platform-specific linker flags there; edit this source directory, not the staged copy.

The [counter sample](examples/counter/main.mbt) includes click handlers, native text input,
a 10,000-row virtual list, application menus, and explicit invalidation. The complete design,
ownership rules, capability boundaries, and native setup are in [docs/MOONBIT.md](../docs/MOONBIT.md).

Generated metadata for the optional editor extension is included, but a typed editor configuration
codec and editor convenience wrapper are not part of this module. The default native host does not
install the editor provider.
