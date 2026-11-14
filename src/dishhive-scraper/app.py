"""Dishhive recipe-scraper sidecar.

Wraps the recipe-scrapers Python library (https://github.com/hhursev/recipe-scrapers)
behind a small HTTP API so the .NET app can fall back to it for sites without a
dedicated C# provider (see docs/plans/RECIPE_SCRAPERS_ADOPTION_PLAN.md).

The service is a pure extractor: the .NET app fetches the page HTML (single outbound
HTTP path, consistent User-Agent) and posts it here; nothing is fetched or persisted
on this side.

Package updates are staged, smoke-tested and atomically activated under
SCRAPER_LIB_DIR. A failed install never replaces the current package.
"""

import logging
import os
import re
import shutil
import subprocess
import sys
import threading
import urllib.request
import uuid
from contextlib import asynccontextmanager, contextmanager
from datetime import datetime, timezone
from importlib.metadata import PackageNotFoundError, distributions, version as dist_version
from json import dumps, loads
from pathlib import Path
from typing import Optional

from fastapi import FastAPI, HTTPException
from packaging.version import InvalidVersion, Version
from pydantic import BaseModel
from source_measurements import published_metric_ingredients

PACKAGE = "recipe-scrapers"
LIB_DIR = os.environ.get("SCRAPER_LIB_DIR", "/data/lib")
LIB_ROOT = Path(LIB_DIR)
VERSIONS_DIR = LIB_ROOT / "versions"
ACTIVE_LINK = LIB_ROOT / "current"
STATE_FILE = LIB_ROOT / "update-state.json"
LOCK_FILE = LIB_ROOT / "update.lock"
PYPI_JSON_URL = f"https://pypi.org/pypi/{PACKAGE}/json"
RESTART_DELAY_SECONDS = 0.5
_update_lock = threading.Lock()

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
log = logging.getLogger("dishhive-scraper")

@asynccontextmanager
async def lifespan(_app):
    recover_interrupted_update()
    yield


app = FastAPI(title="Dishhive recipe-scraper sidecar", lifespan=lifespan)


def installed_version() -> str:
    if ACTIVE_LINK.exists():
        active = next(
            (d.version for d in distributions(path=[str(ACTIVE_LINK)])
             if (d.name or "").replace("_", "-").lower() == PACKAGE),
            None,
        )
        if active:
            return active
    try:
        return dist_version(PACKAGE)
    except PackageNotFoundError:
        return "unknown"


def latest_pypi_version() -> Optional[str]:
    try:
        with urllib.request.urlopen(PYPI_JSON_URL, timeout=10) as resp:
            return loads(resp.read())["info"]["version"]
    except Exception as e:
        log.warning("Could not query PyPI for the latest %s version: %s", PACKAGE, e)
        return None


def is_newer(candidate: str, current: str) -> bool:
    try:
        return Version(candidate) > Version(current)
    except InvalidVersion:
        return False


class ScrapeRequest(BaseModel):
    html: str
    url: str


class UpdateRequest(BaseModel):
    # None means "latest from PyPI"; a specific version pins (up- or downgrade)
    version: Optional[str] = None


def validate_requested_version(value: Optional[str]) -> Optional[str]:
    if value is None:
        return None
    candidate = value.strip()
    if not candidate or len(candidate) > 80 or not re.fullmatch(r"[0-9A-Za-z][0-9A-Za-z.!+_-]*", candidate):
        raise ValueError("Invalid package version")
    try:
        return str(Version(candidate))
    except InvalidVersion as exc:
        raise ValueError("Invalid package version") from exc


def _write_state(**values) -> None:
    LIB_ROOT.mkdir(parents=True, exist_ok=True)
    current = {}
    try:
        current = loads(STATE_FILE.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        pass
    current.update(values)
    temporary = STATE_FILE.with_suffix(".tmp")
    temporary.write_text(dumps(current, ensure_ascii=False, indent=2), encoding="utf-8")
    os.replace(temporary, STATE_FILE)


@contextmanager
def update_guard():
    if not _update_lock.acquire(blocking=False):
        raise RuntimeError("An update is already running")
    handle = None
    try:
        LIB_ROOT.mkdir(parents=True, exist_ok=True)
        handle = LOCK_FILE.open("a+b")
        try:
            import fcntl
            fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except ImportError:
            pass
        except BlockingIOError as exc:
            raise RuntimeError("An update is already running") from exc
        yield
    finally:
        try:
            if handle is not None:
                handle.close()
        finally:
            _update_lock.release()


def _installed_target_version(path: Path) -> Optional[str]:
    return next(
        (d.version for d in distributions(path=[str(path)])
         if (d.name or "").replace("_", "-").lower() == PACKAGE),
        None,
    )


def _smoke_test(path: Path) -> None:
    sample = """<script type="application/ld+json">{"@context":"https://schema.org","@type":"Recipe","name":"Smoke soup","recipeIngredient":["1 onion"],"recipeInstructions":["Cook"]}</script>"""
    code = (
        "from recipe_scrapers import scrape_html; "
        f"s=scrape_html({sample!r}, org_url='https://example.test/recipe', supported_only=False); "
        "assert s.title() == 'Smoke soup'; assert s.ingredients()"
    )
    environment = os.environ.copy()
    environment["PYTHONPATH"] = str(path)
    subprocess.run([sys.executable, "-c", code], check=True, capture_output=True,
                   text=True, timeout=30, env=environment)


def _activate(path: Path) -> Optional[str]:
    previous = str(ACTIVE_LINK.resolve()) if ACTIVE_LINK.exists() else None
    temporary = LIB_ROOT / f".current-{uuid.uuid4().hex}"
    os.symlink(path, temporary, target_is_directory=True)
    try:
        os.replace(temporary, ACTIVE_LINK)
    finally:
        temporary.unlink(missing_ok=True)
    return previous


def _record_failure(error: Exception) -> None:
    try:
        _write_state(status="failed", activeVersion=installed_version(),
                     updatedAt=datetime.now(timezone.utc).isoformat(), error=str(error)[-500:])
    except OSError:
        log.exception("Could not persist update failure")


def recover_interrupted_update() -> None:
    """Reconcile the durable activation marker before serving or loading scraper modules."""
    with update_guard():
        try:
            state = loads(STATE_FILE.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            return
        if state.get("status") not in ("activating", "restarting"):
            return
        target = Path(state.get("targetPath") or "")
        previous = state.get("previousPath")
        try:
            if not target.is_dir() or target.resolve().parent != VERSIONS_DIR.resolve():
                raise RuntimeError("Interrupted update has no valid staged version")
            _smoke_test(target)
            _activate(target)
            _write_state(status="succeeded", activeVersion=_installed_target_version(target),
                         updatedAt=datetime.now(timezone.utc).isoformat(), error=None)
        except Exception as exc:
            if previous:
                prior = Path(previous)
                if not prior.is_dir() or prior.resolve().parent != VERSIONS_DIR.resolve():
                    raise RuntimeError("Interrupted update has no valid rollback version") from exc
                _activate(prior)
            else:
                ACTIVE_LINK.unlink(missing_ok=True)
            _record_failure(exc)


def _prune_versions(active: Path, previous: Optional[str]) -> None:
    keep = {str(active.resolve())}
    if previous:
        keep.add(str(Path(previous).resolve()))
    candidates = sorted(
        (path for path in VERSIONS_DIR.iterdir() if path.is_dir()),
        key=lambda path: path.stat().st_mtime,
        reverse=True,
    )
    for path in candidates:
        if str(path.resolve()) in keep:
            continue
        if len(keep) < 3:
            keep.add(str(path.resolve()))
            continue
        shutil.rmtree(path)


@app.get("/healthz")
def healthz():
    return {"status": "ok", "package": PACKAGE, "version": installed_version()}


@app.get("/version")
def version_info():
    installed = installed_version()
    latest = latest_pypi_version()
    return {
        "installedVersion": installed,
        "latestVersion": latest,
        "updateAvailable": latest is not None and is_newer(latest, installed),
    }


@app.get("/update-status")
def update_status():
    try:
        return loads(STATE_FILE.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {"status": "neverRun", "activeVersion": installed_version()}


@app.post("/update", status_code=202)
def update(req: UpdateRequest):
    try:
        requested = validate_requested_version(req.version)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    spec = f"{PACKAGE}=={requested}" if requested else PACKAGE
    staging = LIB_ROOT / "staging" / uuid.uuid4().hex
    try:
        with update_guard():
            staging.mkdir(parents=True)
            result = subprocess.run(
                [sys.executable, "-m", "pip", "install", "--no-cache-dir", "--target", str(staging), spec],
                capture_output=True, text=True, timeout=300,
            )
            if result.returncode != 0:
                raise RuntimeError(f"pip install failed: {result.stderr[-500:]}")
            new_version = _installed_target_version(staging)
            if new_version is None or (requested is not None and Version(new_version) != Version(requested)):
                raise RuntimeError("Installed package version did not match the requested version")
            _smoke_test(staging)

            VERSIONS_DIR.mkdir(parents=True, exist_ok=True)
            destination = VERSIONS_DIR / f"{new_version}-{uuid.uuid4().hex[:8]}"
            os.replace(staging, destination)
            previous = str(ACTIVE_LINK.resolve()) if ACTIVE_LINK.exists() else None
            _write_state(status="activating", targetPath=str(destination), previousPath=previous,
                         updatedAt=datetime.now(timezone.utc).isoformat(), error=None)
            try:
                _activate(destination)
                _prune_versions(destination, previous)
                _write_state(status="restarting", activeVersion=new_version,
                             previousPath=previous, updatedAt=datetime.now(timezone.utc).isoformat(), error=None)
            except Exception:
                # Roll back under the same lock. The process still has the old
                # modules loaded, so reporting failure must also restore the disk.
                if previous is None:
                    ACTIVE_LINK.unlink(missing_ok=True)
                else:
                    _activate(Path(previous))
                raise
    except RuntimeError as exc:
        status = 409 if "already running" in str(exc) else 502
        if status != 409:
            _record_failure(exc)
        raise HTTPException(status_code=status, detail=str(exc)) from exc
    except (OSError, subprocess.SubprocessError) as exc:
        _record_failure(exc)
        raise HTTPException(status_code=502, detail=f"Update failed: {str(exc)[-500:]}") from exc
    finally:
        if staging.exists():
            shutil.rmtree(staging, ignore_errors=True)

    log.info("Installed and activated %s %s; restarting", PACKAGE, new_version)
    threading.Timer(RESTART_DELAY_SECONDS, lambda: os._exit(0)).start()
    return {"status": "restarting", "version": new_version}


@app.post("/scrape")
def scrape(req: ScrapeRequest):
    from recipe_scrapers import scrape_html

    try:
        # supported_only=False enables wild mode: generic schema.org extraction
        # for sites without a dedicated scraper class
        scraper = scrape_html(req.html, org_url=req.url, supported_only=False)
    except Exception as e:
        raise HTTPException(status_code=422, detail=f"No recipe found at {req.url}: {e}")

    def safe(name, default=None):
        try:
            value = getattr(scraper, name)()
            return value if value is not None else default
        except Exception:
            return default

    title = safe("title")
    if not title or not str(title).strip():
        raise HTTPException(status_code=422, detail=f"No recipe title found at {req.url}")

    raw = None
    try:
        import json as _json

        raw = _json.dumps(scraper.to_json(), ensure_ascii=False, default=str)
    except Exception:
        pass

    # schema.org keywords is officially comma-separated Text; some sites/scrapers
    # return a string instead of a list
    keywords = safe("keywords", [])
    if isinstance(keywords, str):
        keywords = [k.strip() for k in keywords.split(",") if k.strip()]

    ingredients = safe("ingredients", [])
    metric_ingredients = published_metric_ingredients(req.html, ingredients)
    if raw is not None and any(metric_ingredients):
        raw_data = loads(raw)
        if isinstance(raw_data, dict):
            raw_data["metricIngredients"] = metric_ingredients
            raw = dumps(raw_data, ensure_ascii=False)
    return {
        "title": str(title).strip(),
        "description": safe("description"),
        "ingredients": ingredients,
        "metricIngredients": metric_ingredients,
        "instructions": safe("instructions_list", []),
        "yields": safe("yields"),
        "image": safe("image"),
        "prepTimeMinutes": _as_int(safe("prep_time")),
        "cookTimeMinutes": _as_int(safe("cook_time")),
        "totalTimeMinutes": _as_int(safe("total_time")),
        "category": safe("category"),
        "keywords": keywords,
        "canonicalUrl": safe("canonical_url"),
        "host": safe("host"),
        "scraperVersion": installed_version(),
        "raw": raw,
    }


def _as_int(value) -> Optional[int]:
    if value is None:
        return None
    if isinstance(value, (int, float)):
        return int(value)
    match = re.search(r"\d+", str(value))
    return int(match.group()) if match else None
