"""Black-box semantic generator tests. Python 3.10+ and .NET 10 only; no GPUI build."""
from __future__ import annotations

import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


class GeneratorTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.build = tempfile.TemporaryDirectory(prefix="gpui-generator-bin-")
        cls.dotnet = shutil.which("dotnet")
        if not cls.dotnet:
            raise RuntimeError("Install .NET SDK 10 to execute the generator tests")
        subprocess.run([cls.dotnet, "build", str(ROOT / "tools/Gpui.Bindings.Generator"),
                        "--nologo", "--output", cls.build.name], check=True)
        cls.dll = Path(cls.build.name) / "Gpui.Bindings.Generator.dll"

    @classmethod
    def tearDownClass(cls) -> None:
        cls.build.cleanup()

    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory(prefix="gpui-generator-fixture-")
        self.root = Path(self.temp.name)
        for relative in ("bindings/schema.json", "bindings/extensions.json", "bindings/moonbit/bridge.json",
                         "src/Gpui.Editor/schema.json"):
            destination = self.root / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(ROOT / relative, destination)

    def tearDown(self) -> None:
        self.temp.cleanup()

    def invoke(self, *args: str, succeeds: bool = True) -> str:
        result = subprocess.run([self.dotnet, str(self.dll), *args, "--root", str(self.root)],
                                capture_output=True, text=True, check=False)
        self.assertEqual(result.returncode == 0, succeeds, result.stdout + result.stderr)
        return result.stdout + result.stderr

    def modify(self, relative: str, update) -> None:
        path = self.root / relative
        value = json.loads(path.read_text())
        update(value)
        path.write_text(json.dumps(value), encoding="utf-8")

    def test_round_trip_and_committed_compatibility(self) -> None:
        listing = self.invoke("list")
        self.invoke("generate")
        self.invoke("verify")
        for row in listing.splitlines():
            target, relative = row.split("\t")
            self.assertIn(target, ("csharp", "rust", "moonbit", "reference"))
            actual = (self.root / relative).read_bytes()
            # Covers unchanged C#/Rust hashes as well as every new semantic/bridge output.
            self.assertEqual(actual, (ROOT / relative).read_bytes(), relative)
            self.assertNotIn(b"\r", actual, relative)
            self.assertFalse(actual.startswith(b"\xef\xbb\xbf"), relative)
        before = {path: path.stat().st_mtime_ns for path in self.root.rglob("*.g.*")}
        self.invoke("generate")
        self.assertEqual(before, {path: path.stat().st_mtime_ns for path in before})

    def test_target_selection_does_not_write_other_frontends(self) -> None:
        self.invoke("generate", "--target", "moonbit")
        self.invoke("verify", "--target", "moonbit")
        self.assertFalse((self.root / "src/Gpui/Rendering/Semantic.g.cs").exists())
        self.invoke("verify", succeeds=False)
        self.assertTrue(all(row.startswith("moonbit\t") for row in self.invoke("list", "--target", "moonbit").splitlines()))

    def test_root_with_trailing_separator_terminates(self) -> None:
        result = subprocess.run([self.dotnet, str(self.dll), "generate", "--root", str(self.root) + "/"],
                                capture_output=True, text=True, check=False, timeout=30)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_stale_selected_output_fails(self) -> None:
        self.invoke("generate")
        (self.root / "moonbit/elements.g.mbt").write_text("stale\n")
        self.invoke("verify", "--target", "csharp,rust", succeeds=True)
        self.invoke("verify", "--target", "moonbit", succeeds=False)

    def test_bad_arguments_fail(self) -> None:
        for args in (("generate", "--target", "mooonbit"), ("generate", "verify"),
                     ("generate", "--mispelled"), ("generate", "--target", "")):
            self.invoke(*args, succeeds=False)

    def test_output_traversal_validated_even_when_unselected(self) -> None:
        self.modify("bindings/extensions.json", lambda x: x["schemas"][0].update(moonbitOutput="moonbit/../../escape.g.mbt"))
        self.invoke("generate", "--target", "csharp", succeeds=False)
        self.assertFalse((self.root / "src/Gpui/Rendering/Semantic.g.cs").exists())

    def test_duplicate_output_rejected_before_writes(self) -> None:
        self.modify("bindings/extensions.json", lambda x: x["schemas"][0].update(moonbitOutput="moonbit/elements.g.mbt"))
        self.invoke("generate", succeeds=False)
        self.assertFalse((self.root / "moonbit/elements.g.mbt").exists())

    def test_noncanonical_output_path_rejected(self) -> None:
        self.modify("bindings/extensions.json", lambda x: x["schemas"][0].update(moonbitOutput="moonbit//elements.g.mbt"))
        self.invoke("generate", succeeds=False)
        self.assertFalse((self.root / "moonbit/elements.g.mbt").exists())

    def test_unknown_bridge_type_rejected(self) -> None:
        self.modify("bindings/moonbit/bridge.json", lambda x: x["functions"][0].update(result="rust_struct"))
        self.invoke("generate", succeeds=False)

    def test_duplicate_bridge_name_rejected(self) -> None:
        self.modify("bindings/moonbit/bridge.json", lambda x: x["functions"].append(x["functions"][0]))
        self.invoke("generate", succeeds=False)

    def test_links_are_not_followed_for_outputs(self) -> None:
        with tempfile.TemporaryDirectory() as outside:
            try:
                (self.root / "moonbit").symlink_to(outside, target_is_directory=True)
            except OSError:
                self.skipTest("Creating symlinks is not enabled on this host")
            self.invoke("generate", succeeds=False)
            self.assertEqual(list(Path(outside).iterdir()), [])


if __name__ == "__main__":
    unittest.main(verbosity=2)
