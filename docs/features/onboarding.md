# First-run onboarding

## Goal

Give a new household a short, skippable setup wizard when Dishhive starts with an
empty database. The wizard saves the two planning/display defaults and lets the
household add its first members before entering the week planner.

## Behaviour

- On startup, the API starts onboarding only when there is no household, recipe,
  meal-plan, cookbook, or user-setting data.
- Demo mode suppresses onboarding immediately so its background data seed cannot race
  the first browser request.
- An `onboardingStatus` user setting records `InProgress`, `Completed`, or `Skipped`.
  This keeps an interrupted setup resumable and prevents it from appearing again.
- The full-screen wizard defaults to Monday and metric, so every step can be accepted
  without editing.
- Adding household members is optional. Members are saved immediately through the
  existing family-members API.
- Completing or skipping the wizard opens the application root (the week planner).
- Once the API says onboarding is no longer needed, a versioned browser cache marker
  makes later launches skip the onboarding request synchronously. Clearing site data
  causes the backend to be checked again; bumping the cache-key version re-enables the
  check for a future onboarding revision. Replacing the backend database while keeping
  the same browser origin likewise requires clearing site data or bumping that version.
- The wizard UI is deferred and is loaded only when onboarding is actually required.
- If the onboarding check is unavailable, Dishhive falls back to the normal app shell.

## Implementation checklist

- [x] Add an idempotent empty-database onboarding lifecycle API
- [x] Persist completed and skipped outcomes without a schema change
- [x] Add the full-screen preferences and household wizard
- [x] Resume members already added during an interrupted wizard
- [x] Hide the normal application shell while onboarding is active
- [x] Add backend and frontend coverage for first-run, resume, complete, and skip paths
