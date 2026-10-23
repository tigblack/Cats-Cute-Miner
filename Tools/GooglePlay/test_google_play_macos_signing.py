"""Signing guard tests with synthetic files confined to build/, never release receipts."""
import importlib.util
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest

TOOLS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('google_play_macos', TOOLS / 'google_play_macos.py')
workflow = importlib.util.module_from_spec(spec)
spec.loader.exec_module(workflow)


class SigningGuards(unittest.TestCase):
    def setUp(self):
        build = TOOLS.parents[1] / 'build'
        build.mkdir(exist_ok=True)
        self.tmp = tempfile.TemporaryDirectory(prefix='signing-guard-', dir=build)
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name) / 'project'
        self.evidence = self.root / 'release/google-play/evidence'
        self.evidence.mkdir(parents=True)
        self.cert = 'A' * 64
        self.profile = {'release': {'play_app_id': 'test-app', 'signing_evidence_ref': 'release/google-play/evidence/signing.json'}}
        self.receipt = {'package': workflow.PACKAGE, 'status': 'existing_upload_key_verified', 'certificateSha256': self.cert}
        private = Path(self.tmp.name) / 'private'
        private.mkdir(mode=0o700)
        key = private / 'fake.jks'
        key.write_bytes(b'synthetic signing guard fixture; not a real keystore')
        self.secret = private / 'signing.secret.json'
        self.secret.write_text(json.dumps({'package': workflow.PACKAGE, 'keystore': str(key), 'alias': 'upload', 'storePassword': 'synthetic-test-value', 'keyPassword': 'synthetic-test-value', 'certSha256': self.cert}))
        self.secret.chmod(0o600)
        self.args = SimpleNamespace(signing_json=str(self.secret))
        self.persist()

    def persist(self):
        (self.root / 'release/google-play/profile.json').write_text(json.dumps(self.profile))
        (self.evidence / 'signing.json').write_text(json.dumps(self.receipt))

    def first_upload(self, package=None):
        self.receipt.update(status='first_upload_key_verified', first_upload_evidence_ref='release/google-play/evidence/first.json')
        self.first = {'package': package or workflow.PACKAGE, 'play_app_id': 'test-app', 'upload_key_certificate_registered': False, 'read_only': True, 'observed_upload_key_message': 'Certificate fingerprints will be shown here after you upload your first app bundle'}
        (self.evidence / 'first.json').write_text(json.dumps(self.first))
        self.persist()

    def test_existing_verified_key_uses_active_receipt(self):
        _, expected = workflow.signing(self.args, self.root)
        self.assertEqual(expected, self.cert)

    def test_mismatched_certificate_rejected(self):
        self.receipt['certificateSha256'] = 'B' * 64
        self.persist()
        with self.assertRaises(workflow.Blocked):
            workflow.signing(self.args, self.root)

    def test_first_key_requires_matching_console_first_upload_evidence(self):
        self.first_upload()
        _, expected = workflow.signing(self.args, self.root)
        self.assertEqual(expected, self.cert)
        self.first['upload_key_certificate_registered'] = True
        (self.evidence / 'first.json').write_text(json.dumps(self.first))
        with self.assertRaises(workflow.Blocked):
            workflow.signing(self.args, self.root)

    def test_first_key_cannot_use_another_games_receipt(self):
        self.first_upload(package='com.example.anothergame')
        with self.assertRaises(workflow.Blocked):
            workflow.signing(self.args, self.root)

    def test_first_key_cannot_use_unconfirmed_console_state(self):
        self.first_upload()
        self.first['observed_upload_key_message'] = ''
        (self.evidence / 'first.json').write_text(json.dumps(self.first))
        with self.assertRaises(workflow.Blocked):
            workflow.signing(self.args, self.root)

    def test_receipt_must_remain_inside_curated_evidence(self):
        self.profile['release']['signing_evidence_ref'] = '../outside.json'
        self.persist()
        with self.assertRaises(workflow.Blocked):
            workflow.signing(self.args, self.root)


if __name__ == '__main__':
    unittest.main()
