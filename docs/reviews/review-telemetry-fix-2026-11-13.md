# Review actions and input revisions — 13 November 2026

R9's remaining telemetry work is implemented and deployed locally. The application is
healthy with all 24 recipes retained. Freezy remains enabled and diagnostic recipe text
retention remains disabled.

## Review behavior

The AI task-performance page separates model-call outcomes from observed human review
actions. It shows facts reviews, facts corrected on the same source revision, text
edited after a recorded translation, distinct reviewed/edited suggestions and suggestions
whose inputs changed. Expand review history to inspect recipe links, changed fields,
changed inputs and source/current versions.

Facts set after editing source content are counted separately from corrections to an
earlier AI assessment. Repeated confirmations remain separate actions. These are review
signals, not independently measured accuracy scores.

Suggestion edits are compared structurally, including dates, courses, content,
attendance and reservations. No telemetry depends on English error-message text.
Every review attempt is retained, so a successful retry does not erase an earlier
edited/rejected attempt. Recipe/facts versions, household changes, required collection
membership and freezer changes are distinguished from submitted edits. Retries count
once in the distinct-suggestion summaries.

Events carry recorded operation links for model/configuration filtering. Actions without
that evidence remain in the all-events view and are excluded from model-specific
cohorts. Retention is bounded to 2,500 events.

## Input evidence

New model calls retain source versions and hashes for their enclosing operation.
Planning captures its context fingerprint and known recipe/confirmation versions;
facts, translation and extraction capture source content hashes. Nested calls retain
the parent context and add their own evidence. Parallel scopes remain isolated and
capability probes do not inherit recipe input metadata.

Snapshots store identifiers, versions and hashes, without recipe or household text.
The per-call prompt hash still identifies the actual model messages. Older calls have
unknown input versions rather than fabricated defaults.

## Verification

The final complete local run passed **595 backend tests, zero skipped**, including
PostgreSQL upgrade/race checks and the actual API/Chrome journey; **67 frontend tests**;
**24 browser checks**; and **9 Linux scraper tests**. Generated contracts were unchanged
on regeneration. Six new mobile/desktop telemetry screenshots were inspected.

The full run found an SDK race in which response cleanup could replace cancellation
with a closed-stream exception. The gate now preserves timeout/caller-cancellation
outcomes and never retries after the deadline. Tests cover both wrapped outcomes;
the actual SDK deadline regression also passed ten repeats.

Evidence: `artifacts/review-telemetry-2026-11-13/local-final/`,
`artifacts/r9-cancellation-tests.log`, `artifacts/r9-deadline-repeat-*.log`, deployed
API/image records, and checked-in browser regression sources/snapshots.

The user explicitly approved live evaluations through OpenRouter's
`deepseek/deepseek-v4-flash`, including local recipe and household context, on
13 November. The earlier automatic-review rejection is resolved. Subsequent
live results and independent accuracy evidence are recorded in the
[current checklist](application-review-status-2026-11-12.md).

The approved final live run now supplies direct input-evidence validation: **288
provider calls across 27 planning operations**, including query refinement, facts,
intent and generation, have complete input revisions/fingerprints. Ordinals are
contiguous and never exceed 32; no call has an unfinished terminal state. See
`artifacts/source-discovery-2026-11-13/verified-audit.json` and the source/facts report.
