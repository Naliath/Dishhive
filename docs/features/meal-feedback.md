# Feature: Meal Eaten / Rating Feedback

**Status legend:** `[ ]` = new · `[~]` = in progress · `[x]` = done
**Related:** [past-dishes-and-statistics.md](past-dishes-and-statistics.md), [week-planner.md](week-planner.md), [ai-week-planning.md](ai-week-planning.md)

## Feature Goal

Close the feedback loop on the plan: mark planned meals as actually cooked/eaten (or
skipped) and let household members rate them 1–5 stars. Feeds statistics
("planned 9×, loved 8×") and AI meal suggestions.

## Scope

**In scope**
- Eaten status per planned meal: unmarked (default) / eaten / skipped
- Per-member 1–5 star rating per meal ("loved" = 4 or higher); re-rating overwrites
- Statistics integration: times eaten, average rating, loved count per dish;
  meals eaten + average rating given per member
- Feedback UI on the history page; subtle one-tap eaten toggle on past planner days

**Out of scope**
- Free-text reviews/comments per meal
- Rating individual courses separately (rating applies to the planned dish row)
- Freezy write-back on eaten (future feature, see possible-features.md)

## Domain Model

```
PlannedMeal
├── Eaten: EatenStatus? (null = unmarked; Eaten = 0, Skipped = 1)
└── Ratings: MealRating[]

MealRating                              // composite PK, like PlannedMealAttendee
├── PlannedMealId (FK, cascade)
├── FamilyMemberId (FK, cascade)
└── Rating: int (1–5, validated in API)
```

- A rater must exist but need **not** be an attendee (someone may have joined unplanned).
- Feedback only applies to meals on or before today (400 for future dates).
- Migration: `AddMealFeedback`.

## Backend

- `PUT /api/plannedmeals/{id}/eaten` body `{ "status": 0 | 1 | null }` (null clears) → meal DTO
- `PUT /api/plannedmeals/{id}/ratings/{memberId}` body `{ "rating": 1..5 }` (upsert) → meal DTO
- `DELETE /api/plannedmeals/{id}/ratings/{memberId}` → 204
- `PlannedMealDto` carries `eaten` and `ratings[]`
- Statistics: `DishStatisticDto` gains `timesEaten`, `averageRating`, `lovedCount`;
  `MemberStatisticsDto` gains `mealsEaten`, `averageRatingGiven`

## Frontend

- History page: per meal row an eaten cycle-toggle (unmarked → eaten → skipped → unmarked)
  and a star button (shows the average, e.g. "4.5 ★") opening the rating dialog
- `components/meal-rating-dialog/`: one row of 5 tappable stars per member (attendees
  first); each tap saves immediately, tapping the current rating clears it
- Week planner: past-day cards get only a small eaten checkmark toggle (planner stays clean)
- Statistics page: "Eaten" and "Rating" columns

## Implementation Checklist

- [x] `EatenStatus` + `MealRating` entities, DbContext config, `AddMealFeedback` migration
- [x] Eaten/rating endpoints + validation (future date, rating range, unknown member)
- [x] Statistics aggregates (times eaten, average rating, loved count, member feedback)
- [x] Integration tests (feedback endpoints, statistics aggregates, cascade)
- [x] Rating dialog component
- [x] History page eaten toggle + rate button
- [x] Planner past-day eaten checkmark
- [x] Statistics page columns
- [x] Suggestion engine consumes ratings (see ai-week-planning.md)
