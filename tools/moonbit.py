#!/usr/bin/env python3
"""Repository-local MoonBit development commands. Requires Python 3.10+.

No command downloads a native host or launches a GUI implicitly. Only `run` opens
the sample, and it requires an explicit library path. Platform build metadata is
written to a disposable copy, never to the tracked MoonBit package files.
"""
from __future__ import annotations

import argparse
import json
import os
import stat
from pathlib import Path
import re
import shutil
import struct
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
FFI = ROOT / "moonbit/internal/ffi"
MARKER = ".gpui-moonbit-owned"


def tool(name: str) -> str:
    found = shutil.which(name)
    if not found:
        raise RuntimeError(f"Required executable not found: {name}. See docs/MOONBIT.md.")
    return found


def execute(command: list[str | Path], *, cwd: Path = ROOT,
            env: dict[str, str] | None = None) -> None:
    arguments = [str(value) for value in command]
    print("+ " + subprocess.list2cmdline(arguments), flush=True)
    subprocess.run(arguments, cwd=cwd, env=env, check=True)


def _remove_tree(path: Path) -> None:
    """Remove a tree, retrying transient Windows locks (AV scans, test binaries)."""

    def make_writable(function, target: str, _excinfo) -> None:
        try:
            os.chmod(target, stat.S_IWRITE)
        except OSError:
            pass
        function(target)

    last: OSError | None = None
    for attempt in range(6):
        try:
            shutil.rmtree(path, onerror=make_writable)
            return
        except OSError as error:
            last = error
            time.sleep(0.5 * (attempt + 1))
    assert last is not None
    raise last


def owned_directory(name: str) -> Path:
    """Replace only an explicitly marked, repository-contained build directory."""
    destination = ROOT / "artifacts" / name
    if not destination.resolve().is_relative_to(ROOT) or destination.is_symlink():
        raise RuntimeError(f"Build directory escapes the checkout: {destination}")
    if destination.exists():
        marker = destination / MARKER
        if marker.is_symlink() or not marker.is_file() or marker.read_text() != "gpui-moonbit\n":
            raise RuntimeError(f"Refusing to replace unmarked build directory: {destination}")
        _remove_tree(destination)
    destination.mkdir(parents=True)
    (destination / MARKER).write_text("gpui-moonbit\n")
    return destination


def schema_hash() -> str:
    source = (ROOT / "moonbit/protocol/semantic.g.mbt").read_text(encoding="utf-8")
    match = re.search(r"SCHEMA_HASH\s*:\s*UInt64\s*=\s*(0x[0-9A-Fa-f]{16})UL", source)
    if not match:
        raise RuntimeError("Generated MoonBit schema hash is missing or malformed")
    return match.group(1) + "ull"


def generation(command: str, target: str) -> None:
    execute([tool("dotnet"), "run", "--project", ROOT / "tools/Gpui.Bindings.Generator",
             "--", command, "--root", ROOT, "--target", target])
    if "all" in target.split(",") or "moonbit" in target.split(","):
        execute([tool("cargo"), "run", "--locked", "--manifest-path",
                 ROOT / "tools/gpui-abi-gen/Cargo.toml", "--", command, "--root", ROOT])


def c_test(compiler: str | None, sanitize: bool) -> None:
    if struct.calcsize("P") != 8:
        raise RuntimeError("The desktop bridge and ABI fixtures currently target 64-bit processes")
    windows = sys.platform == "win32"
    cc = tool(compiler or os.environ.get("CC", "cl" if windows else "cc"))
    if windows and Path(cc).stem.lower() not in ("cl", "clang-cl"):
        raise RuntimeError("Use cl or clang-cl in an x64 developer shell; MinGW is not supported")
    if sanitize and windows:
        raise RuntimeError("This sanitizer command is for GCC/Clang on Linux/macOS")
    output = owned_directory("moonbit-c-tests")
    sources = [FFI / name for name in ("bridge.c", "arena.c", "commands.c")]
    fake = ROOT / "tests/moonbit/fake_host.c"
    test = ROOT / "tests/moonbit/bridge_test.c"
    libraries: list[Path] = []
    if windows:
        common = [cc, "/nologo", "/std:c11", "/W4", "/WX", "/D_CRT_SECURE_NO_WARNINGS",
                  "/I" + str(FFI), "/DGPUI_TEST_SCHEMA_HASH=" + schema_hash()]
        for mode in range(4):
            library = output / f"fake{mode}.dll"
            execute(common + ["/LD", f"/DFAKE_MODE={mode}", fake, "/Fe" + str(library),
                              "/Fo" + str(output / f"fake{mode}.obj")], cwd=output)
            libraries.append(library)
        binary = output / "bridge_test.exe"
        execute(common + [test, *sources, "/Fe" + str(binary)], cwd=output)
    else:
        common = [cc, "-std=c11", "-Wall", "-Wextra", "-Werror", "-pedantic", "-g",
                  "-I", str(FFI), "-DGPUI_TEST_SCHEMA_HASH=" + schema_hash()]
        if sanitize:
            common += ["-fsanitize=address,undefined", "-fno-omit-frame-pointer"]
        suffix = ".dylib" if sys.platform == "darwin" else ".so"
        shared = "-dynamiclib" if sys.platform == "darwin" else "-shared"
        for mode in range(4):
            library = output / f"fake{mode}{suffix}"
            execute(common + [shared, "-fPIC", "-pthread", f"-DFAKE_MODE={mode}", fake,
                              "-o", library], cwd=output)
            libraries.append(library)
        binary = output / "bridge_test"
        linker = [] if sys.platform == "darwin" else ["-ldl"]
        execute(common + [test, *sources, "-pthread", *linker, "-o", binary], cwd=output)
    execute([binary, *libraries], cwd=output)


def stage(compiler: str | None) -> Path:
    destination = owned_directory("moonbit-stage")
    shutil.copytree(ROOT / "moonbit", destination, dirs_exist_ok=True,
                    ignore=shutil.ignore_patterns("_build", "target", ".moon", ".mooncakes", ".DS_Store"))
    windows = sys.platform == "win32"
    if compiler:
        compiler = tool(compiler)
        if windows and Path(compiler).stem.lower() not in ("cl", "clang-cl"):
            raise RuntimeError("Windows MoonBit native requires cl or clang-cl")
    for path in destination.rglob("moon.pkg.json"):
        package = json.loads(path.read_text(encoding="utf-8"))
        native = package.setdefault("link", {}).setdefault("native", {})
        # Do not override cc-flags: MoonBit supplies required runtime compiler flags.
        native["stub-cc-flags"] = "/std:c11 /D_CRT_SECURE_NO_WARNINGS" if windows else "-std=c11 -fPIC"
        if sys.platform.startswith("linux"):
            native["cc-link-flags"] = "-ldl"
            native["stub-cc-link-flags"] = "-ldl"
        if compiler:
            native["cc"] = compiler
            native["stub-cc"] = compiler
        path.write_text(json.dumps(package, indent=2) + "\n", encoding="utf-8")
    print(f"Staged module: {destination}", flush=True)
    return destination


def moon_command(command: str, compiler: str | None, library: str | None) -> None:
    moon = tool("moon")
    environment = os.environ.copy()
    environment["MOONBIT_NEW_NATIVE"] = "0"
    if command == "run":
        if not library:
            raise RuntimeError("run requires --library PATH to an already-built native host")
        path = Path(library).expanduser().resolve(strict=True)
        if not path.is_file():
            raise RuntimeError("The native host must be a library file")
        environment["GPUI_MOON_NATIVE"] = str(path)
    destination = stage(compiler)
    arguments = [moon, command]
    if command == "run":
        arguments.append("examples/counter")
    arguments += ["--target", "native"]
    execute(arguments, cwd=destination, env=environment)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    for name in ("generate", "verify"):
        sub = commands.add_parser(name)
        sub.add_argument("--target", default="all", help="all, or comma-separated csharp,rust,moonbit,reference")
    sub = commands.add_parser("c-test", help="Compile and execute the C ABI peer, without GPUI or MoonBit")
    sub.add_argument("--cc")
    sub.add_argument("--sanitize", action="store_true")
    commands.add_parser("generator-test", help="Test generation/verification in isolated fixture directories")
    commands.add_parser("abi-test", help="Run standalone Rust ABI generator tests; does not build GPUI")
    commands.add_parser("static-test", help="Fast source/manifest and helper tests; not a language compiler")
    for name in ("check", "test", "build", "run", "stage"):
        sub = commands.add_parser(name)
        sub.add_argument("--cc")
        if name == "run":
            sub.add_argument("--library", required=True)
    args = parser.parse_args()
    try:
        if args.command in ("generate", "verify"):
            generation(args.command, args.target)
        elif args.command == "c-test":
            c_test(args.cc, args.sanitize)
        elif args.command == "generator-test":
            execute([sys.executable, ROOT / "tests/moonbit/generator_test.py"])
        elif args.command == "abi-test":
            execute([tool("cargo"), "test", "--locked", "--manifest-path", ROOT / "tools/gpui-abi-gen/Cargo.toml"])
        elif args.command == "static-test":
            execute([sys.executable, ROOT / "tests/moonbit/static_test.py"])
        elif args.command == "stage":
            stage(args.cc)
        else:
            moon_command(args.command, args.cc, getattr(args, "library", None))
        return 0
    except (OSError, RuntimeError, subprocess.CalledProcessError) as error:
        print(f"moonbit: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
