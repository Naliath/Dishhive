# Feature: MCP Server

**Status legend:** `[ ]` = new · `[~]` = in progress · `[x]` = done
**Related:** [week-planner.md](week-planner.md), [meal-feedback.md](meal-feedback.md),
[recipe-store.md](recipe-store.md), [dietary-facts.md](dietary-facts.md)

## Feature Goal

Let local AI assistants (LM Studio, Claude Desktop, any MCP client) query Dishhive
directly: the planned meals for a week, what was eaten (and how it was rated) on a
specific date, and the stored recipe library — via the Model Context Protocol, so the
AI calls tools instead of screen-scraping or being pasted JSON.

## Architecture

- **Official C# SDK**: `ModelContextProtocol.AspNetCore` 1.4.0 (2.x is still preview).
  Adding it pulled in the vulnerable transitive `Microsoft.OpenApi` 2.0.0 (NU1903);
  pinned to 2.9.0 explicitly.
- **Streamable-HTTP endpoint at `/mcp` on the existing API** (:5100 in Docker) — no
  extra process or port; the same container that serves the REST API and the SPA.
  Program.cs maps it next to the controllers and adds `/mcp` to BOTH SPA exclusion
  lists (the static-files `UseWhen` and the `MapFallback`) — without that the SPA
  fallback would answer `/mcp` with index.html.
- **Stateless mode** (`HttpServerTransportOptions.Stateless = true`): the tools are
  simple request/response reads, so no session tracking, and clients reconnect freely.
  (Sampling/elicitation are unavailable in stateless mode — irrelevant here.)
- **Tools** (`Mcp/DishhiveMcpTools.cs`, `[McpServerToolType]`): one instance per
  invocation from request DI, so the scoped `DishhiveDbContext` injects like in a
  controller. All tools are marked `ReadOnly`/`Idempotent`.
- **Trust boundary**: identical to the REST API — unauthenticated on the LAN. The MCP
  surface is read-only, so it exposes nothing the API didn't already.

## Tools

Designed for small local models: dates travel as `yyyy-MM-dd` strings with explicit
error messages ("…use the yyyy-MM-dd format"), member names are resolved server-side
(ids mean nothing to an LLM), payloads stay compact, and empty results explain
themselves ("Nothing was planned on …") instead of returning bare `[]`.

| Tool | Input | Returns |
|---|---|---|
| `get_week_plan` | optional date (any day; normalized to that week's Monday, default: current week) | weekStart/weekEnd + every meal: date, day, meal type, course, dish, vague instruction, attendee names, eaten status, fromFreezer, recipeId |
| `get_eaten_on_date` | date | that day's meals with eaten/skipped/**not marked** (Eaten is nullable) + per-member ratings `{member, rating}` |
| `search_recipes` | optional query / category / comma-separated tags | same filter semantics as `GET /api/recipes` (title+keywords substring, AND-tags); capped at 25 by title with a "narrow the search" note; each hit carries dietary facts (`contains` + assessed) so allergy questions are answerable from search results |
| `get_recipe` | recipe id (from a search hit or a week-plan meal) | full detail: description, ingredient lines (verbatim `OriginalText`), ordered steps, times, tags, dietary facts, source URL |

`recipeId` in the planning tools deliberately chains into `get_recipe`.

## Client setup

- **LM Studio** (`mcp.json`):
  ```json
  { "mcpServers": { "dishhive": { "url": "http://localhost:5100/mcp" } } }
  ```
- **stdio-only clients** (e.g. Claude Desktop):
  ```json
  { "mcpServers": { "dishhive": { "command": "npx", "args": ["mcp-remote", "http://localhost:5100/mcp"] } } }
  ```
- Bare `dotnet run` development: the API listens per launchSettings (:5100), same path.

## Risks / Notes

- Filter logic for `search_recipes` mirrors `RecipesController.GetRecipes` by
  replication (there is no shared query service); if the REST filters evolve, keep the
  tool in sync.
- No write tools by design (planning a meal / marking eaten would need confirmation
  UX that MCP clients don't uniformly provide) — natural later addition, as are
  Freezy/shopping-list tools.

## Implementation Checklist

- [x] `ModelContextProtocol.AspNetCore` 1.4.0 + `Microsoft.OpenApi` 2.9.0 pin
- [x] `DishhiveMcpTools` with the four read-only tools
- [x] Program.cs: `AddMcpServer().WithHttpTransport(stateless).WithTools<>` +
      `MapMcp("/mcp")` + SPA exclusion lists
- [x] Unit tests (week normalization, eaten states, rating names, search parity,
      cap, error strings) + integration tests (SDK client over TestServer:
      tools/list + real tool calls)
- [x] README feature bullet + docs index row
