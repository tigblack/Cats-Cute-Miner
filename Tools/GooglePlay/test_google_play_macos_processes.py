"""Process guards; synthetic argv only, never logs live Unity arguments."""
import importlib.util
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('google_play_macos', Path(__file__).with_name('google_play_macos.py'))
workflow = importlib.util.module_from_spec(spec)
spec.loader.exec_module(workflow)


class UnityProcessGuards(unittest.TestCase):
    unity = '/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/MacOS/Unity'
    main = '/Users/example/Unity Projects/Game'
    mirror = Path(main + '/build/google-play-ci-macos')

    def count(self, args):
        raw = '\n'.join('42 ' + self.unity + ' ' + self.unity + ' ' + a for a in args).encode()
        with patch.object(workflow, 'run', return_value=SimpleNamespace(returncode=0, stdout=raw)):
            return workflow.processes(self.mirror)

    def test_main_editor_and_import_workers_do_not_lock_mirror(self):
        self.assertEqual(0, self.count([
            '-projectPath "' + self.main + '"',
            '-projectPath "' + self.main + '" -batchMode -assetImportWorker -name AssetImportWorker0',
            '-projectPath "' + self.main + '" -batchMode -name AssetImportWorkerHW1',
            '-projectPath "' + self.main + '" -batchMode -name "AssetImportWorkerHW2"',
        ]))

    def test_real_batch_editor_is_still_a_conflict(self):
        self.assertEqual(1, self.count(['-projectPath "' + self.main + '/build/ci" -batchmode']))

    def test_editor_or_worker_on_release_mirror_is_a_conflict(self):
        for suffix in ['', ' -batchMode -assetImportWorker', ' -batchMode -name AssetImportWorkerHW1']:
            self.assertEqual(1, self.count(['-projectPath "' + str(self.mirror) + '"' + suffix]))

    def test_failed_process_inspection_refuses_build(self):
        with patch.object(workflow, 'run', return_value=SimpleNamespace(returncode=1, stdout=b'')):
            with self.assertRaises(workflow.Blocked):
                workflow.processes(self.mirror)


if __name__ == '__main__':
    unittest.main()
