"""Dishhive recipe-scraper sidecar.

Wraps the recipe-scrapers Python library (https://github.com/hhursev/recipe-scrapers)
behind a small HTTP API so the .NET app can fall back to it for sites without a
dedicated C# provider (see docs/plans/RECIPE_SCRAPERS_ADOPTION_PLAN.md).

The service is a pure extractor: the .NET app fetches the page HTML (single outbound
HTTP path, consistent User-Agent) and posts it here; nothing is fetched or persisted
on this side.

Package updates: the image bakes a pinned recipe-scrapers version; POST /update
pip-installs a newer (or specific) version into SCRAPER_LIB_DIR — a volume-backed
directory that PYTHONPATH puts ahead of site-packages — and then exits the process
so the container restart policy brings the service back up on the new version.
"""

import logging
import os
import re
import shutil
import subprocess
import sys
import threading
import urllib.request
from importlib.metadata import PackageNotFoundError, distributions, version as dist_version
from json import loads
from typing import Optional

from fastapi import FastAPI, HTTPException
from packaging.version import InvalidVersion, Version
from pydantic import BaseModel

PACKAGE = "recipe-scrapers"
LIB_DIR = os.environ.get("SCRAPER_LIB_DIR", "/data/lib")
PYPI_JSON_URL = f"https://pypi.org/pypi/{PACKAGE}/json"
RESTART_DELAY_SECONDS = 0.5

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
log = logging.getLogger("dishhive-scraper")

app = FastAPI(title="Dishhive recipe-scraper sidecar")


def installed_version() -> str:
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


@app.post("/update", status_code=202)
def update(req: UpdateRequest):
    spec = f"{PACKAGE}=={req.version}" if req.version else PACKAGE
    log.info("Updating to %s in %s", spec, LIB_DIR)

    # The override dir only ever holds recipe-scrapers + its deps; start clean so
    # leftovers from a previous version can't shadow the new install
    shutil.rmtree(LIB_DIR, ignore_errors=True)
    os.makedirs(LIB_DIR, exist_ok=True)

    result = subprocess.run(
        [sys.executable, "-m", "pip", "install", "--no-cache-dir", "--target", LIB_DIR, spec],
        capture_output=True,
        text=True,
        timeout=300,
    )
    if result.returncode != 0:
        log.error("pip install failed: %s", result.stderr[-2000:])
        raise HTTPException(
            status_code=502,
            detail=f"pip install {spec} failed: {result.stderr[-500:]}",
        )

    # Read the freshly installed version straight from the target dir; the running
    # process still has the old module imported
    new_version = next(
        (d.version for d in distributions(path=[LIB_DIR]) if (d.name or "").replace("_", "-") == PACKAGE),
        None,
    )

    log.info("Installed %s %s; restarting to load it", PACKAGE, new_version)
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

    return {
        "title": str(title).strip(),
        "description": safe("description"),
        "ingredients": safe("ingredients", []),
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
