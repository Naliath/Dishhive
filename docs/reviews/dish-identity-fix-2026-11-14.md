# Requested dish identity and v4.1 evaluation

This followup replaces the earlier source/date-only interpretation of the Friday
pancake result. A source recipe can satisfy the requested date and still be the
wrong food. The original prompt remains unchanged:
`Op vrijdag pannekoeken van @[Dagelijkse Kost] als hoofd maaltijd`.

The user subsequently clarified: **only Belgian/Dutch pancakes count; leave Friday
empty when none qualify**. The corpus preserves the original prompt and supplies
that clarification separately. Its strict source/count/slot assertions remain
visible, while `policyPassed` separately measures an explicitly permitted explained
gap. An empty date requires `partial` status, no dinner main on that date, a nonempty
dated unmet-requirement entry and all other assertions passing. Wrong food, a different
pancake style, an unexplained gap or an allergy violation cannot be excused by this
policy. The offline oracle self-checks are part of `scripts/verify-local.ps1`.

The user approved `deepseek/deepseek-v4.1-flash` at OpenRouter for the remaining
evaluations after the local `.env` changed models. Older v4-flash measurements
remain historical comparisons; they do not establish v4.1-flash quality.

## Implemented identity checks

- The central AI gate now owns the bounded `recipe.dish_match` task and strict
  `recipe-dish-match-v1` contract. Each call assesses up to four recipes using
  bounded titles, ingredients and preparation text. Every supplied ID must appear
  exactly once. A valid negative result differs from an unavailable assessment.
- Food identity is independent of ingredient classes, allergies, source and date.
  A milk-containing pancake can match the food while remaining excluded from
  planning. Suitable meatballs cannot satisfy a pancake request.
- Intent v11 preserves the requested food. Source research exposes only candidates
  that independently satisfy the ingredient and food requirements. Verified research
  provenance is distinct from merely visiting a page.
- Content-bound identity approvals survive review and acceptance; changing the
  recipe content or selecting another saved recipe invalidates the approval.
  The generated payload cannot grant itself an approval. Source candidates cannot
  leak into other dates or source-count clauses.
- Missing assessed options retain the user's partial-plan policy: leave the date
  empty and explain the gap. Website HTTP failures retain partial results without
  adding network retries. The shared 28-page, 32-provider-attempt and 300-second
  operation limits remain.
- The AI statistics page labels and filters dish-matching calls in English and
  Dutch. Real deployed metrics, mobile/desktop geometry and screenshots were checked
  at 360px and 1280px without database writes.

Regression tests cover malformed/missing/forged IDs, valid negatives, unavailable
classification, input metadata, task profiles, content-bound approvals, changed
recipe acceptance, source-scope leakage and wrong-food candidates with otherwise
eligible facts.

## Independent identity evidence

The expanded 23-case corpus distinguishes translations, spelling mistakes, ordinary
variants, explicit regional restrictions, misleading titles, side dishes and unrelated
meals. It passed **69/69** on v4.1-flash over three repeats (average 750ms, P95 1318ms).
The unchanged v1 matcher correctly rejects Japanese and American variants for an
explicit Belgian/Dutch request. These console calls
used prompted JSON; deployed calls also exercised the verified strict-schema mode.
Neither result establishes arbitrary future dish-recognition accuracy.

Corpus SHA256:
`8B1371F4ADA43F9E40383E712B84DDCC976BD79E9CB576CD1F07B0068C1C92BE`.
Evidence: `artifacts/final-item-2026-11-13/dish-matching-v4.1-scoped-full.json`.
The earlier 20-case/60-call run remains historical generic-family evidence; its
corpus hash was `63EC0AE7DF7A5C24C6DB2DB32FE8EFF8AB89FA9BDC9C9B465E89BF45EAECDE32`.

The Friday end-to-end oracle now checks actual recipe URLs against independently
reviewed food-family labels and requires assessed facts. The labels are test-only;
production discovery receives no reference URL or expected-answer list. Replaying
the old apparent Friday successes gives **0/3**: source/date checks had admitted
unrelated food. This is a corrected oracle, not a regression hidden by changing the
user's prompt or weakening allergy requirements.

## Facts profile comparisons

All rows retain the same independent 50-case corpus, including all 20 public demo
recipes. Three repeats give 150 logical assessments. Unknown/no-result counts as
a failed assertion. No declaration or expected class was changed.

| Model / facts setting | Result | Disposition |
| --- | --- | --- |
| Historical v4-flash, low reasoning / 2048, contract v7 | 150/150 | Historical model only. |
| v4.1-flash, low reasoning / 2048, contract v7 | 135/150 | Rejected; truncated reasoning left no usable JSON. |
| v4.1-flash, low reasoning / 4096, contract v7 | 149/150 | One shrimp-paste assessment still exhausted both attempts. |
| v4.1-flash, low reasoning / 8192, contract v7 | **150/150** | Selected local profile; all 150 physical calls succeeded, average 3977ms, P95 14265ms. |
| v4.1-flash, reasoning disabled / 512, experimental plural clarification v9 | 145/150 | Rejected and reverted; missed celery/peanuts and added gluten to declared lupin flour. |

The prompt experiment was not retained. The final facts contract remains v7.
Recipe titles remain excluded from the provider's facts input. A no-result never
becomes an assessed empty ingredient list. The earlier per-ingredient v8 experiment
was also rejected and remains separate historical evidence.

The selected profile's largest observed reasoning usage was 6634 tokens, below the
8192 allowance; the slowest logical assessment took 32710ms. These console runs
used prompted JSON. The production execution gate also exercises strict schema
after its capability check; response-mode differences remain an explicit boundary
on transferring console measurements to planning. The larger allowance is a cap,
not a requirement for every call to consume that many tokens. Portable checked-in
defaults remain reasoning disabled, 512 output tokens and concurrency one. The
measured local `.env` uses facts reasoning enabled, 8192 tokens and concurrency four.

Enabling reasoning for query refinement did not help: all three Friday trials
failed and took 136–231 seconds. That experiment and its extra configuration were
removed. Refinement stays bounded at 400 output tokens with no reasoning or semantic
retry. Contract v7 asks for established alternative preparations while preserving
the requested food and any explicit cuisine; it supplies no expected recipe URL.

The first v7-focused Friday request remained partial. A premature container restart
interrupted its next two requests; those two transport errors are invalid quality
measurements. They remain in the raw focused report and are excluded from claims
about model quality. The complete final corpus runs on a fresh, fixed deployment.

Facts corpus SHA256:
`91A9CCE05787DCEAFBE2C08DA05BA460BB3EB6C7DAE6EE25AE3F0C2B10332E9B`.
Raw reports, execution audits and screenshots are under
`artifacts/final-item-2026-11-13/`; that directory is ignored by Git.



## Final planning results

All nine scenarios ran three times on image `sha256:2598f1dea829c3ec494f113c82f89bf0f1414d17c03f6a173a293db8203709c3`, with the same
approved v4.1-flash model and selected facts profile. Final strict completion is
**17/27**. Assertions were not weakened to turn partial output into completion.

| Scenario | Passed | Durations (seconds) |
| --- | --- | --- |
| `distinct-vegetarian-source` | 0/3 | 126.5, 145.7, 116.8 |
| `multi-source-courses-and-explicit-chicken` | 0/3 | 190.6, 179.5, 219.8 |
| `dutch-distinct-source-request` | 2/3 | 109.6, 44.5, 147.5 |
| `local-recipes-stay-linked` | 3/3 | 2.5, 4.2, 7.9 |
| `quick-local-recipes` | 3/3 | 5.1, 14.1, 2.5 |
| `conflicting-hard-requirements-are-explicit` | 3/3 | 1.7, 2.8, 3.5 |
| `mandatory-saved-collection-membership` | 3/3 | 2.7, 1.9, 5.0 |
| `mandatory-quick-auto-collection` | 3/3 | 2.1, 1.6, 1.6 |
| `dutch-friday-pancakes-from-source` | 0/3 | 50.3, 153.1, 115.9 |

Failed completion assertions:

- `distinct-vegetarian-source`, repeat 1: Plan status 'partial' was not allowed; expected complete; dagelijksekost.vrt.be main: expected at least 4 distinct recipes, got 3.
- `distinct-vegetarian-source`, repeat 2: Plan status 'partial' was not allowed; expected complete; dagelijksekost.vrt.be main: expected at least 4 distinct recipes, got 3.
- `distinct-vegetarian-source`, repeat 3: Plan status 'partial' was not allowed; expected complete; dagelijksekost.vrt.be main: expected at least 4 distinct recipes, got 3.
- `multi-source-courses-and-explicit-chicken`, repeat 1: Plan status 'partial' was not allowed; expected complete; dagelijksekost.vrt.be main: 'Tajine van kip met groene olijven en zoute citroen' contains excluded class Poultry.
- `multi-source-courses-and-explicit-chicken`, repeat 2: Plan status 'partial' was not allowed; expected complete; dagelijksekost.vrt.be main: expected at least 4 distinct recipes, got 3; Slot 2027-01-22/main: expected 1-1 suggestion(s), got 0; Slot 2027-01-22/main: no verified dish contains Poultry; Slot 2027-01-22/main: explicit requirement was not linked to a normalized constraint; Slot 2027-01-22/main: explicit dietary exception has no review warning.
- `multi-source-courses-and-explicit-chicken`, repeat 3: Plan status 'partial' was not allowed; expected complete; dagelijksekost.vrt.be main: expected at least 4 distinct recipes, got 3; Slot 2027-01-22/main: expected 1-1 suggestion(s), got 0; Slot 2027-01-22/main: no verified dish contains Poultry; Slot 2027-01-22/main: explicit requirement was not linked to a normalized constraint; Slot 2027-01-22/main: explicit dietary exception has no review warning.
- `dutch-distinct-source-request`, repeat 2: Plan status 'partial' was not allowed; expected complete; dagelijksekost.vrt.be main: expected at least 4 distinct recipes, got 0.
- `dutch-friday-pancakes-from-source`, repeat 1: dagelijksekost.vrt.be main: expected at least 1 distinct recipes, got 0; dagelijksekost.vrt.be main: missing required date 2027-02-26; Slot 2027-02-26/main: expected 1-1 suggestion(s), got 0.
- `dutch-friday-pancakes-from-source`, repeat 2: dagelijksekost.vrt.be main: expected at least 1 distinct recipes, got 0; dagelijksekost.vrt.be main: missing required date 2027-02-26; Slot 2027-02-26/main: expected 1-1 suggestion(s), got 0.
- `dutch-friday-pancakes-from-source`, repeat 3: dagelijksekost.vrt.be main: expected at least 1 distinct recipes, got 0; dagelijksekost.vrt.be main: missing required date 2027-02-26; Slot 2027-02-26/main: expected 1-1 suggestion(s), got 0.

Performance warnings are advisory and do not change correctness scores.
The final correlated audit covers **319 provider calls / 27 operations**,
maximum **31 calls** and **23 pages**
per operation, with contiguous ordinals, no missing input metadata and no unfinished
semantic outcomes. Raw evidence: `planning-final-dish-identity.json` and
`final-semantic-audit/`. Interrupted prototype requests are excluded from this final window.

## Local deployment and recovery

The full local command passed **683 backend tests with zero skips, 84 frontend tests,
28 fixture browser checks, one real API/PostgreSQL/Chrome workflow, one production
PWA lifecycle and nine Linux scraper tests**. Generated contracts, catalogs, fixture
typing, production build and offline partial-policy oracle checks passed. EN/NL task filters and labels were inspected at
360px/1280px using actual metrics (`dish-match-stats/`).

A full 25-table backup/restore, row/image digest comparison and candidate startup
passed on this exact image (`restore-final-dish-identity/summary.json`). The normal
development deployment restores Freezy and background jobs, disables durable diagnostic
text, and retains the approved v4.1 model with facts reasoning/8192/concurrency four
(`normal-deployment.json`). The dataset remains 24 recipes, four members, 14 meals,
including four unchanged human-confirmed rows/fact sets (`human-check-final.json`).

The user selected partial output when assessed options run out and for transient
website failures, without new network retries. Missing source meals remain visible
completion gaps. Food matching is implemented; finite semantic accuracy and finding
enough eligible source recipes are separate claims. Deferred product features were
not implemented in this numbered queue. No commit or hosted deployment was made.

## Clarified Friday policy

The subsequent live scoped run preserves the original prompt and supplies the user's
Belgian/Dutch-only clarification separately (`instructionClarification`). It passes
**3/3 policy checks**: four assessed saved meals remain, Friday is empty, and its
missing meal has a dated explanation. Strict Friday source/slot completion remains
**0/3**, separately visible in `passed`; `policyPassed`/`acceptedPartial` record the
approved behavior without counting a missing pancake as a supplied pancake. The
legacy strict runner exit status remains unchanged.

The recorded scoped response was also replayed into the deployed review dialog at
[360px](../../artifacts/final-item-2026-11-13/review-replay-final/partial-review-360.png)
and [1280px](../../artifacts/final-item-2026-11-13/review-replay-final/partial-review-1280.png).
Both captures were inspected. Four linked saved meals and dated empty-Friday/weekend
explanations remain readable; actions fit within the viewport. This is a rendering
check using the recorded live response and real read APIs; it makes no acceptance
or database writes.

Scoped request durations (seconds): 115.4, 202.8, 94.1.
The additional execution audit has **63 provider calls /
three operations**, maximum 27 calls and
21 resolved pages, no missing metadata or unfinished
outcomes (`scoped-friday-audit/`). Offline `-ValidateOracle` checks accept the explained
gap and reject an unexplained gap, Japanese/American substitutions and allergy
violations. The expanded 23-case matcher corpus passes 69/69, including the explicit
regional restrictions, without changing the production matcher or candidate image.

## Remaining source quality

- All three English repeats retain three of four requested source meals. Their
  19/19/23 inspected pages have no resolution failures: these are retrieval shortfalls,
  not website-outage explanations.
- All three mixed repeats retain only three vegetarian source mains. Two also leave
  the explicit Friday chicken slot empty. Dessert coverage is 2/2 despite one failed
  page in each run. The first repeat's VRT chicken was correctly claimed by its own
  chicken constraint with a diet-review warning; the broad source oracle also flags
  its Poultry class. That flag must not be read as the application silently assigning
  chicken to a vegetarian clause. The underlying missing fourth vegetarian meal and
  partial status still make the request incomplete.
- The failed Dutch repeat has four empty searches after fetching the source sitemap
  exceeded its 15-second HTTP timeout. It inspected zero recipe pages. This follows
  the user's accepted partial/outage policy; it is not evidence of a classifier or
  date-parsing defect. The other two Dutch repeats complete.
- Source-heavy requests still exceed their 60/90-second advisory latency budgets.
  These remain advisory; correctness and approved partial behavior are reported
  independently. Broader source discovery and source-clause oracle isolation remain
  concrete iteration targets.

The changed-file whitespace check passes. The whole-worktree check still flags
pre-existing staged whitespace in `possible-features.md`,
`HouseholdCalendarIntegrationTests.cs` and `offline-read-cache.service.spec.ts`.
Those unrelated staged files were retained.
