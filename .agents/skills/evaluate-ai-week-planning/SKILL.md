---
name: evaluate-ai-week-planning
description: Run, assess, and extend Dishhive's AI week-planning regression corpus. Use when investigating inaccurate or slow week suggestions, reproducing model or search variability, comparing AI configurations, reviewing evaluation reports, adding a prompt regression case, or tuning the planning pipeline without losing existing behavior.
---

# Evaluate AI week planning

Use the repository corpus as the durable specification and its PowerShell runner as the objective judge. Apply qualitative AI judgment only after the deterministic assertions pass or to explain why they failed.

## Locate the evaluation surface

Run commands from the repository root returned by `git rev-parse --show-toplevel`.

- Read `src/Dishhive.Api.Tests/Fixtures/AiWeekPlanning/scenarios.json` for prompts and assertions.
- Read `scripts/run-ai-week-planning-evals.ps1` before changing its assertion schema.
- Read the development evaluation section in `docs/features/ai-week-planning.md` when changing the workflow.
- Treat `artifacts/ai-evals/` as disposable raw evidence; never make it the source of truth.

## Run an evaluation

Confirm that the intended local Dishhive API and its configured model/search services are healthy. Never point this runner at production unless the user explicitly authorizes it.

Run the smallest useful case while iterating:

```powershell
./scripts/run-ai-week-planning-evals.ps1 `
  -ScenarioId multi-source-courses-and-explicit-chicken `
  -Iterations 3
```

Run the complete corpus before concluding:

```powershell
./scripts/run-ai-week-planning-evals.ps1 -Iterations 1
```

Use at least three iterations when measuring nondeterminism or comparing model/configuration changes. Use `-FailOnPerformance` only when the scenario budget should be a hard gate.

## Assess results

Inspect the generated JSON report, not only the exit code. For every iteration, verify:

1. Every explicit count, weekday, course, source, canonical ingredient class, and attendee requirement is satisfied.
2. A dish or external recipe is not repeated across dates unless repetition was explicitly requested.
3. Source URLs resolve to individual recipes rather than search, category, magazine, or list pages.
4. Explicit meat requests remain one shared dish and expose a dietary review warning when appropriate.
5. Reasons accurately describe the selected dish and do not claim unsupported facts.
6. Timing changes are explained using stored planning metrics and correlated application logs: model turns, tokens, searches, recipe resolutions, parse failures, repair, and fallback.

Classify a failure before editing code:

- **Intent failure:** normalized planning intent or final model output misunderstood count, date, course, language, or source scope.
- **Candidate failure:** search returned too few results, a collection page, an unsuitable recipe, or resolution failed.
- **Validation/repair failure:** invalid output was accepted, valid output was displaced, or repair created duplicates/wrong courses.
- **Fallback failure:** rules backfill silently violated an explicit instruction.
- **Performance failure:** identify the dominant search, resolution, model, retry, or reasoning duration rather than blaming total latency.

Correlate warnings and debug logs by request ID. State explicitly whether a change was made by the model, deterministic repair, post-processing, or rules fallback.

## Preserve a regression

Add every confirmed user-facing regression to the JSON array. Prefer requirements over exact recipe titles so the scenario remains useful as search results evolve.

- Give the scenario a stable kebab-case `id` and preserve the original prompt verbatim.
- Set `uniqueDishes` and `rejectCollectionPages` unless the prompt explicitly permits otherwise.
- Express exact source counts with equal minimum and maximum values.
- Use source/course/date and canonical `requiredClasses`/`excludedClasses` assertions for verified external recipes.
- Never add title keywords as a proxy for dietary facts or fuzzy language understanding.
- Use slot assertions for explicitly dated dishes and `allAttendees` for shared meals.
- Add multilingual cases when parsing or interpretation depends on language.
- Set a realistic performance budget that reports regressions without trading away correctness.
- If behavior is intentionally unsupported, set `status` to `known-limitation` and explain it in `limitation`. Keep it explicitly runnable; do not game the case with token-specific production branches.

Run `AiWeekPlanningScenarioCorpusTests` after editing the corpus. Do not weaken an assertion merely to accommodate one model run; diagnose the failure or document why the product requirement changed.

## Verify a change

For implementation work, finish with all applicable gates:

```powershell
dotnet test src/Dishhive.Api.Tests/Dishhive.Api.Tests.csproj --no-restore
npm test -- --watch=false
./scripts/run-ai-week-planning-evals.ps1 -Iterations 1
git diff --check
```

Run the frontend command from `src/dishhive-web`. Report unit-test totals, scenario pass/fail counts, timings, report path, model/configuration used, and any validation gap. Do not call a stochastic result stable from a single focused pass.
