# Deployment security

Dishhive's default Compose profile is a **trusted-local** deployment. The app,
PostgreSQL, scraper, and SearXNG ports bind to `127.0.0.1`; other devices cannot
reach them directly. The development database password is only a local default and
must be replaced with `POSTGRES_PASSWORD` outside this profile.

## Remote or shared access

Do not change the app binding to `0.0.0.0` without adding a TLS reverse proxy and
authentication. Set separate long random `DISHHIVE_API_KEY` and
`DISHHIVE_MCP_READ_API_KEY` values. Configure the proxy to add the REST credential
on authenticated `/api` requests; MCP clients receive only the read credential.
Either `X-Dishhive-Api-Key` or a Bearer header is supported. The MCP credential cannot
authorize REST mutations, and the REST credential does not authorize `/mcp`.
When REST protection is enabled without an MCP key, MCP is disabled. Keep PostgreSQL, the scraper,
and SearXNG on the internal Compose network; they are not public application APIs.

The API applies a 12 MiB request-body ceiling and rate-limits expensive AI, import,
scraper-update, and bulk-assessment endpoints to ten requests per minute and two
concurrent operations, with no waiting queue. The AI gate separately bounds model
calls and queued requests. A public reverse proxy should add its
own per-client rate limits, request limits, security headers, and access logging.

Example secret generation in PowerShell:

```powershell
[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

Place the result and a unique database password in the gitignored `.env`. Never
commit cloud AI keys or proxy credentials. Back up the `postgres_data` volume and
test restoration before treating the instance as durable household storage.

## Diagnostic privacy

`AI_RETAIN_DIAGNOSTIC_TEXT` defaults to `false`. Task telemetry still records bounded
errors, prompt hashes, configuration fingerprints, timing, budgets, and token counts,
but raw household instructions and normalized intent are retained only when this
option is explicitly enabled for diagnosis. With background jobs enabled, a maintenance
pass purges this text after 24 hours, or at its next pass when retention is disabled.
Interrupted response validation is recorded as abandoned; stale nonterminal records
are recovered after at least 15 minutes.

The optional `docker-compose.remote.yml` overlay validates the required credentials
and disables demo seeding. It keeps the app on loopback for an authenticated TLS proxy
on the host. It does not expose a public plaintext listener or supply a login service.
