# =============================================================================
# Dishhive Recipe-Scraper Sidecar Dockerfile
# =============================================================================
# FastAPI wrapper around the recipe-scrapers Python library, used as the
# fallback recipe import provider (see src/dishhive-scraper/app.py).
#
# The image bakes a pinned recipe-scrapers version; POST /update installs a
# newer one into /data/lib (volume) which PYTHONPATH puts ahead of the baked
# install, and the restart policy reloads the process on the new version.
# =============================================================================

FROM python:3.12-slim

WORKDIR /app

COPY src/dishhive-scraper/requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY src/dishhive-scraper/app.py .

# Non-root user; /data/lib must stay writable for package updates
RUN groupadd -r appgroup && useradd -r -g appgroup appuser \
    && mkdir -p /data/lib && chown -R appuser:appgroup /data
USER appuser

# Volume-backed package overrides win over the baked site-packages install
ENV PYTHONPATH=/data/lib
ENV PYTHONUNBUFFERED=1

EXPOSE 8000

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD python -c "import urllib.request; urllib.request.urlopen('http://localhost:8000/healthz', timeout=4)" || exit 1

CMD ["uvicorn", "app:app", "--host", "0.0.0.0", "--port", "8000"]
