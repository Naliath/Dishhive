import tempfile
import unittest
import sys
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

from fastapi import HTTPException

import app


class ScraperUpdateTests(unittest.TestCase):
    def test_rejects_unsafe_or_invalid_versions(self):
        for value in ("../latest", "15.0/evil", "", "not a version"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                app.validate_requested_version(value)

    def test_normalizes_valid_version(self):
        self.assertEqual("15.11.0", app.validate_requested_version("15.11.0"))

    def test_failed_install_keeps_current_target(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            active = root / "active"
            active.mkdir()
            marker = active / "marker"
            marker.write_text("working", encoding="utf-8")
            with patch.multiple(app,
                                LIB_ROOT=root,
                                VERSIONS_DIR=root / "versions",
                                ACTIVE_LINK=active,
                                STATE_FILE=root / "state.json",
                                LOCK_FILE=root / "update.lock"):
                failed = type("Result", (), {"returncode": 1, "stderr": "network failed"})()
                with patch.object(app.subprocess, "run", return_value=failed):
                    with self.assertRaises(HTTPException) as failure:
                        app.update(app.UpdateRequest(version="15.11.1"))
                self.assertEqual(502, failure.exception.status_code)
                self.assertEqual("working", (app.ACTIVE_LINK.resolve() / "marker").read_text(encoding="utf-8"))
                self.assertEqual("failed", app.loads(app.STATE_FILE.read_text(encoding="utf-8"))["status"])

    def test_update_guard_rejects_concurrent_update(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with patch.multiple(app, LIB_ROOT=root, LOCK_FILE=root / "update.lock"):
                with app.update_guard():
                    with self.assertRaisesRegex(RuntimeError, "already running"):
                        with app.update_guard():
                            pass

    def test_scrape_normalizes_string_keywords_and_times(self):
        class FakeScraper:
            def title(self): return "Soup"
            def description(self): return None
            def ingredients(self): return ["1 onion"]
            def instructions_list(self): return ["Cook"]
            def yields(self): return "2 servings"
            def image(self): return None
            def prep_time(self): return "PT15M"
            def cook_time(self): return 20
            def total_time(self): return None
            def category(self): return "main"
            def keywords(self): return "quick, winter"
            def canonical_url(self): return "https://example.test/soup"
            def host(self): return "example.test"
            def to_json(self): return {"name": "Soup"}

        module = SimpleNamespace(scrape_html=lambda *_args, **_kwargs: FakeScraper())
        with patch.dict(sys.modules, {"recipe_scrapers": module}):
            result = app.scrape(app.ScrapeRequest(html="<html></html>", url="https://example.test/soup"))

        self.assertEqual(["quick", "winter"], result["keywords"])
        self.assertEqual(15, result["prepTimeMinutes"])
        self.assertEqual(20, result["cookTimeMinutes"])


if __name__ == "__main__":
    unittest.main()
