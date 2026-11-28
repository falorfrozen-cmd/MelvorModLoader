"""Integration checks: python3 Dev/test-package-linux.py GAME_DIR."""
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from zipfile import ZipFile

GAME = Path(sys.argv.pop(1)).resolve()
SCRIPT = Path(__file__).with_name('package-linux.sh').resolve()
PAYLOAD = 'data_MelvorModLoader_linuxbsd_x86_64/'


class Packaging(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory(prefix='melvor package ')
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.output = self.root / 'Linux loader.zip'

    def run_packager(self, game, env=None):
        self.assertTrue(SCRIPT.is_file(), 'Linux packager is missing')
        return subprocess.run(['/bin/bash', str(SCRIPT), str(game), str(self.output)],
                              cwd=self.root, env=env, capture_output=True, text=True, timeout=120)

    def test_native_package(self):
        game = self.root / 'Steam Library' / 'Melvor Idle 2'
        game.parent.mkdir()
        game.symlink_to(GAME, target_is_directory=True)
        result = self.run_packager(game)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        with ZipFile(self.output) as archive:
            self.assertIsNone(archive.testzip())
            names = set(archive.namelist())
            self.assertEqual({n.split('/')[0] for n in names}, {'override.cfg', PAYLOAD[:-1]})
            self.assertIn(PAYLOAD + 'MelvorModLoader.dll', names)
            self.assertIn(PAYLOAD + 'libhostfxr.so', names)
            for notice in ('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT'):
                self.assertTrue(archive.read(PAYLOAD + notice).strip())
            self.assertFalse({'GodotSharp.dll', 'GamesByMalcs.Melvor2.dll', 'MelvorIdle2.x86_64', 'libsteam_api.so'} & {Path(n).name for n in names})
            self.assertEqual(archive.read('override.cfg'), b'[dotnet]\nproject/assembly_name="MelvorModLoader"\n')

    def test_invalid_game(self):
        for game in (self.root / 'missing', self.root / 'Windows'):
            with self.subTest(game=game):
                result = self.run_packager(game)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn('native Linux', result.stderr)
                self.assertFalse(self.output.exists())

    def test_existing_output(self):
        self.output.write_bytes(b'keep this exactly')
        result = self.run_packager(GAME)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('already exists', result.stderr)
        self.assertEqual(self.output.read_bytes(), b'keep this exactly')

    def test_missing_tools(self):
        for tool, other in [('dotnet', 'python3'), ('python3', 'dotnet')]:
            with self.subTest(tool=tool), tempfile.TemporaryDirectory(dir=self.root) as path:
                other_path = next(Path(p) / other for p in os.environ['PATH'].split(os.pathsep)
                                  if (Path(p) / other).is_file())
                (Path(path) / other).symlink_to(other_path)
                result = self.run_packager(GAME, dict(os.environ, PATH=path))
                self.assertNotEqual(result.returncode, 0)
                self.assertIn(tool, result.stderr)
                self.assertFalse(self.output.exists())


if __name__ == '__main__':
    unittest.main()
