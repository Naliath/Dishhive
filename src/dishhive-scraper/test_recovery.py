import tempfile
import unittest
import json
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch


import app
from fastapi import HTTPException


class ScraperRecoveryCrossTests(unittest.TestCase):
    def test_restart_completes_activation_before_serving(self):
        for status in ("activating", "restarting"):
            with self.subTest(status=status), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                versions = root / "versions"
                previous, target = versions / "old", versions / "new"
                previous.mkdir(parents=True)
                target.mkdir()
                active = root / "current"
                active.symlink_to(previous, target_is_directory=True)
                state = root / "state.json"
                state.write_text(json.dumps({"status": status, "targetPath": str(target), "previousPath": str(previous)}))
                with patch.multiple(app, LIB_ROOT=root, VERSIONS_DIR=versions, ACTIVE_LINK=active,
                                    STATE_FILE=state, LOCK_FILE=root / "update.lock"), \
                     patch.object(app, "_smoke_test"), \
                     patch.object(app, "_installed_target_version", return_value="15.11.1"):
                    app.recover_interrupted_update()
                self.assertEqual(target.resolve(), active.resolve())
                self.assertEqual("succeeded", json.loads(state.read_text())["status"])

    def test_restart_rolls_back_unloadable_active_version(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            versions = root / "versions"
            previous, target = versions / "old", versions / "new"
            previous.mkdir(parents=True)
            target.mkdir()
            active = root / "current"
            active.symlink_to(target, target_is_directory=True)
            state = root / "state.json"
            state.write_text(json.dumps({"status": "restarting", "targetPath": str(target), "previousPath": str(previous)}))
            with patch.multiple(app, LIB_ROOT=root, VERSIONS_DIR=versions, ACTIVE_LINK=active,
                                STATE_FILE=state, LOCK_FILE=root / "update.lock"), \
                 patch.object(app, "_smoke_test", side_effect=RuntimeError("broken package")):
                app.recover_interrupted_update()
            self.assertEqual(previous.resolve(), active.resolve())
            self.assertEqual("failed", json.loads(state.read_text())["status"])

    def test_failure_after_activation_preserves_previous_active_version(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            versions = root / "versions"
            previous = versions / "old"
            previous.mkdir(parents=True)
            active = root / "current"
            active.symlink_to(previous, target_is_directory=True)
            with patch.multiple(app, LIB_ROOT=root, VERSIONS_DIR=versions, ACTIVE_LINK=active,
                                STATE_FILE=root / "state.json", LOCK_FILE=root / "update.lock"):
                with patch.object(app.subprocess, "run", return_value=SimpleNamespace(returncode=0)), \
                     patch.object(app, "_installed_target_version", return_value="15.11.1"), \
                     patch.object(app, "_smoke_test"), \
                     patch.object(app, "_prune_versions", side_effect=OSError("disk error after activation")):
                    with self.assertRaises(HTTPException):
                        app.update(app.UpdateRequest(version="15.11.1"))
                self.assertEqual(previous.resolve(), active.resolve())

    def test_lock_file_open_failure_does_not_permanently_lock_updates(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with patch.multiple(app, LIB_ROOT=root, LOCK_FILE=root / "update.lock"):
                try:
                    with patch.object(Path, "open", side_effect=OSError("disk unavailable")):
                        with self.assertRaises(OSError):
                            with app.update_guard():
                                pass
                    with app.update_guard():
                        pass
                finally:
                    if app._update_lock.locked():
                        app._update_lock.release()


if __name__ == "__main__":
    unittest.main(verbosity=2)
