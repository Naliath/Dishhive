# Recipe contracts and freezer reservations — 13 November 2026

R12 and the remaining reconciliation work in R6 are implemented. The local app at
`http://localhost:5100` has been redeployed without clearing the development database:
24 recipes remain, the health check passes, and Freezy is reachable.

## What can be reviewed

- Open **Freezer links** in the week planner. Future reservations are compared with
  the current Freezy snapshot. Missing items, shortages, changed item details and
  reservations without a reviewed snapshot are shown separately. A failed or malformed
  Freezy response is unavailable, rather than evidence that items were deleted.
- **Edit meal** opens the existing editor to reduce, replace or remove a reservation.
  **Unlink freezer** retains the meal and its notes. **Confirm item** acknowledges the
  displayed item only when sufficient stock remains and its fingerprint still matches.
  All operations change Dishhive only; Freezy remains read-only.
- New suggestions retain the original request's freezer fingerprint in the durable
  review snapshot. Acceptance rejects changed details as `needsReview`. Legacy
  suggestions without that evidence create an explicitly unverified reservation.
- Recipe writes now belong to `RecipeMutationService`: create/edit/delete, images,
  facts confirmation, collection membership, revision invalidation and durable facts
  work. Relational edits/deletes commit tag pruning in the same transaction.
- All frontend API services now use generated Angular clients. Wire model aliases
  reuse generated DTOs and enums. UI-only filters and draft defaults remain local.
  The library's generated pagination endpoint returns items and total count together;
  the original list endpoint and count header remain compatible.
- Validation, missing resources, concurrency conflicts, duplicate/stale references,
  transient dependency failures and unexpected errors use problem details with a
  status, stable code and trace ID. Client adapters preserve those details and status
  codes. An unsuccessful save retains the recipe draft.

## Contract generation

OpenAPI operations have unique controller/action IDs, lowercase API paths, required
non-null fields and named numeric enums. The local runner regenerates and compares
content hashes before running the other checks. File uploads use inline binary schemas
because of the pinned generator's [referenced-file-schema issue](https://github.com/RicoSuter/NSwag/issues/5387).
The exported document uses OpenAPI 3.0. Generated code is never patched manually.

## Verification

The full local run passed 584 backend tests with zero skips, including disposable
PostgreSQL databases and the actual API/Chrome workflow; 67 frontend tests; 22 browser
checks; and 9 Linux scraper tests. The final review-to-acceptance fingerprint check
was subsequently covered by 57 targeted cases. The combined tree then passed the
complete 595-test backend run, zero skipped, with the
[review telemetry work](review-telemetry-fix-2026-11-13.md).

The new browser journeys exercise shortages, changed details, confirmation, unlinking,
retained meals and unavailable stock at 360px and 1280px. All four new screenshots were
reviewed, together with the deployed empty state. Existing visual baselines were retained.
Backend tests also cover missing/malformed stock, stale review requests, legacy
reservations, pre-acceptance metadata changes and concurrent PostgreSQL reassignment.

Evidence: `artifacts/persistence-contracts-2026-11-13/`, `artifacts/r6-final-targeted.log`,
`artifacts/r6-browser-final.log`, and the committed browser snapshot directory.

## Boundaries

Reconciliation describes stock observed at the displayed check time. Independent
Freezy edits can occur afterward; Dishhive cannot lock the other application. Refresh
checks it again. No atomic transaction across applications is claimed. Legacy
reservations remain unverified until reviewed explicitly.

R9 correction/input-revision telemetry and the source/facts accuracy evaluation remain
open and are the next implementation work. Passing transport and workflow tests is
not evidence of model accuracy.
