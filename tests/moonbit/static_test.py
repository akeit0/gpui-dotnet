"""Fast contract checks, deliberately not a replacement for the three language compilers."""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import re
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


class ContractTests(unittest.TestCase):
    def test_manifests_parse(self) -> None:
        for path in (ROOT / "moonbit").rglob("*.json"):
            json.loads(path.read_text())
        self.assertEqual(json.loads((ROOT / "moonbit/moon.mod.json").read_text())["supported-targets"], "native")

    def test_schema_hash_matches_native(self) -> None:
        native = (ROOT / "crates/gpui-dotnet/src/semantic.g.rs").read_text()
        moon = (ROOT / "moonbit/protocol/semantic.g.mbt").read_text()
        pattern = r"SCHEMA_HASH[^=]*=\s*(0x[0-9A-Fa-f]+)"
        self.assertEqual(re.search(pattern, native)[1].lower(), re.search(pattern, moon)[1].lower())

    def test_bridge_manifest_matches_symbols_and_borrows(self) -> None:
        schema = json.loads((ROOT / "bindings/moonbit/bridge.json").read_text())
        ffi = ROOT / "moonbit/internal/ffi"
        moon = (ffi / "ffi.g.mbt").read_text()
        header = (ffi / "bridge.g.h").read_text()
        sources = "\n".join(path.read_text() for path in ffi.glob("*.c"))
        for function in schema["functions"]:
            name = "gpui_moon_" + function["name"]
            self.assertEqual(moon.count('"' + name + '"'), 1, name)
            self.assertEqual(len(re.findall(r"\b" + name + r"\s*\(", header)), 1, name)
            self.assertRegex(sources, r"\b" + name + r"\s*\([^;]*?\)\s*\{")
            parameters = [p["name"] for p in function["parameters"] if p["type"] in ("bytes", "buffer")]
            if parameters:
                block = moon[:moon.index(f'pub extern "C" fn {function["name"]}(')].split("///|")[-1]
                self.assertIn("#borrow(" + ", ".join(parameters) + ")", block)
        self.assertIn("FixedArray[Byte]", moon)

    def test_protocol_references_exist(self) -> None:
        definitions = set(re.findall(r"pub (?:const|fn) (\w+)", (ROOT / "moonbit/protocol/semantic.g.mbt").read_text()))
        for source in (ROOT / "moonbit").glob("*.mbt"):
            for name in re.findall(r"@protocol\.(\w+)", source.read_text()):
                self.assertIn(name, definitions, f"{source.name}: {name}")

    def test_sample_fluent_names_exist(self) -> None:
        sources = "\n".join(path.read_text() for path in (ROOT / "moonbit").glob("*.mbt"))
        methods = set(re.findall(r"fn (?:Element|Frame|App|Window|EventContext|ListController)::(\w+)", sources))
        sample = (ROOT / "moonbit/examples/counter/main.mbt").read_text()
        for name in re.findall(r"\.([a-z_]\w*)\(", sample):
            self.assertIn(name, methods | {"push", "decode_lossy"}, name)
        # pub is read-only in MoonBit; consumer-created menu values require pub(all).
        self.assertIn("pub(all) enum MenuEntry", (ROOT / "moonbit/menu.mbt").read_text())

    def test_helper_protects_unmarked_directories(self) -> None:
        spec = importlib.util.spec_from_file_location("gpui_moon_tool", ROOT / "tools/moonbit.py")
        helper = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(helper)
        with tempfile.TemporaryDirectory() as temporary:
            helper.ROOT = Path(temporary)
            directory = helper.ROOT / "artifacts/build"
            directory.mkdir(parents=True)
            (directory / "user-file").write_text("keep")
            with self.assertRaises(RuntimeError):
                helper.owned_directory("build")
            self.assertEqual((directory / "user-file").read_text(), "keep")
            marked = helper.owned_directory("new")
            (marked / "old-output").write_text("replace")
            helper.owned_directory("new")
            self.assertFalse((marked / "old-output").exists())


if __name__ == "__main__":
    unittest.main(verbosity=2)
