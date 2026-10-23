"""Public legal URL boundary checks; no SDK keys, external calls or signing files."""
import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('google_play_macos', Path(__file__).with_name('google_play_macos.py'))
workflow = importlib.util.module_from_spec(spec)
spec.loader.exec_module(workflow)
BASE = 'https://sites.aimulticast.org/sargsyan/cats-cute-miner-legal/'


class LegalDocumentBoundary(unittest.TestCase):
    def valid(self, privacy, terms, package=workflow.PACKAGE):
        with patch.object(workflow, 'read_json', return_value={'packageName': package, 'privacyUrl': privacy, 'termsUrl': terms}):
            return workflow.legal(Path('.'))

    def test_published_game_documents_and_canonical_studio_routes(self):
        self.assertTrue(self.valid(BASE + 'privacy/', BASE + 'terms/'))
        self.assertTrue(self.valid('https://multicastgames.com/cats-cute-miner/privacy/', 'https://multicastgames.com/cats-cute-miner/terms/'))

    def test_shared_studio_policy_and_wrong_game_do_not_unlock_sdk(self):
        self.assertFalse(self.valid('https://multicastgames.com/policy', 'https://multicastgames.com/termsofuse'))
        self.assertFalse(self.valid(BASE + 'privacy/', BASE + 'terms/', package='com.other.game'))

    def test_documents_cannot_be_swapped_or_reused(self):
        self.assertFalse(self.valid(BASE + 'terms/', BASE + 'privacy/'))
        self.assertFalse(self.valid(BASE + 'privacy/', BASE + 'privacy/'))

    def test_other_sites_and_noncanonical_variants_are_rejected(self):
        for value in [None, '', BASE + 'privacy/?redirect=other', BASE + 'privacy/#draft',
                      BASE.replace('https:', 'http:') + 'privacy/',
                      BASE.replace('/sargsyan/', '/other/') + 'privacy/',
                      BASE.replace('sites.aimulticast.org', 'sites.aimulticast.org.attacker.invalid') + 'privacy/']:
            with self.subTest(value=value):
                self.assertFalse(self.valid(value, BASE + 'terms/'))


if __name__ == '__main__':
    unittest.main()
