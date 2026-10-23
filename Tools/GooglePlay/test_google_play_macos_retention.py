"""Never remove the previous AAB until its replacement verifies completely."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('workflow', Path(__file__).with_name('google_play_macos.py'))
workflow = importlib.util.module_from_spec(spec)
spec.loader.exec_module(workflow)


class ArtifactRetentionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.folder = self.root / 'release/google-play/builds'
        self.folder.mkdir(parents=True)
        self.policy = self.folder.parent / 'artifact-retention.json'
        self.policy.write_text(json.dumps({'package': workflow.PACKAGE, 'mode': 'single_verified_aab'}))
        self.current = self.folder / 'cat-miners-0.1.3-4-noads.aab'
        self.current.write_bytes(b'new verified artifact')
        self.old = self.folder / 'cat-miners-0.1.2-3-noads.aab'
        self.old.write_bytes(b'previous artifact')
        digest = hashlib.sha256(self.current.read_bytes()).hexdigest()
        self.report = {'status': 'artifact_static_checks_passed', 'errors': [], 'sha256': digest,
                       'package': workflow.PACKAGE, 'versionCode': '4'}
        self.inspection = self.folder.parent / 'inspection.json'
        self.inspection.write_text(json.dumps(self.report))
        self.receipt = Path(str(self.current) + '.build.json')
        self.receipt.write_text(json.dumps({'sha256': digest, 'package': workflow.PACKAGE,
                                           'versionCode': 4, 'signatureVerified': True}))

    def cleanup(self):
        return workflow.retain_latest_aab(self.root, self.current, self.inspection)

    def test_only_old_game_artifacts_and_partial_copies_are_removed(self):
        partial = Path(str(self.old) + '.partial')
        partial.write_bytes(b'partial')
        unrelated = self.folder / 'another-game.aab'
        unrelated.write_bytes(b'preserve')
        removed = self.cleanup()
        self.assertEqual({self.old.name, partial.name}, set(removed))
        self.assertTrue(self.current.exists())
        self.assertTrue(self.receipt.exists())
        self.assertTrue(unrelated.exists())

    def test_failed_inspection_keeps_previous_artifact(self):
        self.report['status'] = 'artifact_checks_failed'
        self.inspection.write_text(json.dumps(self.report))
        with self.assertRaises(workflow.Blocked):
            self.cleanup()
        self.assertTrue(self.old.exists())

    def test_changed_artifact_keeps_previous_artifact(self):
        self.current.write_bytes(b'incomplete or changed')
        with self.assertRaises(workflow.Blocked):
            self.cleanup()
        self.assertTrue(self.old.exists())

    def test_newer_build_prevents_all_deletions(self):
        newer = self.folder / 'cat-miners-0.1.4-5-noads.aab'
        newer.write_bytes(b'preserve newer')
        with self.assertRaises(workflow.Blocked):
            self.cleanup()
        self.assertTrue(self.old.exists())
        self.assertTrue(newer.exists())

    def test_symlink_prevents_all_deletions(self):
        self.old.unlink()
        self.old.symlink_to(self.current)
        with self.assertRaises(workflow.Blocked):
            self.cleanup()
        self.assertTrue(self.old.is_symlink())
        self.assertTrue(self.current.exists())

    def test_without_owner_retention_policy_previous_build_is_preserved(self):
        self.policy.unlink()
        self.assertEqual([], self.cleanup())
        self.assertTrue(self.old.exists())


if __name__ == '__main__':
    unittest.main()
