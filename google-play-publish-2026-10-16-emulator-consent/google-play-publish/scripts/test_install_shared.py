"""Filesystem checks use only synthetic skills and a temporary home."""
import contextlib
import io
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import install_shared as installer


class InstallSharedTest(unittest.TestCase):
    def test_bundle_install_repeat_and_update(self):
        with tempfile.TemporaryDirectory() as temp, contextlib.redirect_stdout(io.StringIO()):
            root = Path(temp)
            home = root / "home"
            bundle = root / "bundle"
            names = (installer.NAME, *installer.BUNDLED)
            for agent in installer.AGENT_DIRS:
                (home / agent / "skills").mkdir(parents=True)
            for name in names:
                source = bundle / name
                source.mkdir(parents=True)
                (source / "SKILL.md").write_text("bundled " + name)

            creative = installer.BUNDLED[0]
            original = home / ".codex" / "skills" / creative
            original.mkdir()
            (original / "SKILL.md").write_text("original creative")
            canonical = home / ".agents" / "skills"

            def run(source, stamp):
                with patch.object(Path, "home", return_value=home), patch.object(
                    installer.time, "strftime", return_value=stamp
                ):
                    installer.main(["--source", str(source)])

            run(bundle / installer.NAME, "fresh")
            for name in names:
                self.assertEqual((canonical / name / "SKILL.md").read_text(), "bundled " + name)
                for agent in installer.AGENT_DIRS:
                    link = home / agent / "skills" / name
                    self.assertTrue(installer.is_link(link))
                    self.assertEqual(link.resolve(), canonical / name)
            self.assertEqual(
                (canonical / ".backup/fresh/codex" / creative / "SKILL.md").read_text(),
                "original creative",
            )

            run(canonical / installer.NAME, "repeat")
            self.assertFalse((canonical / ".backup/repeat").exists())
            self.assertEqual({p.name for p in canonical.iterdir()}, {*names, ".backup"})

            assets = canonical / installer.NAME / "assets"
            assets.mkdir()
            (assets / ".env").write_text("synthetic private config")
            for name in names:
                (bundle / name / "SKILL.md").write_text("updated " + name)
            run(bundle / installer.NAME, "update")
            self.assertEqual((assets / ".env").read_text(), "synthetic private config")
            for name in names:
                self.assertEqual((canonical / name / "SKILL.md").read_text(), "updated " + name)
                self.assertTrue((canonical / ".backup/update" / name / "SKILL.md").is_file())
                for agent in installer.AGENT_DIRS:
                    self.assertEqual((home / agent / "skills" / name).resolve(), canonical / name)


if __name__ == "__main__":
    unittest.main()
