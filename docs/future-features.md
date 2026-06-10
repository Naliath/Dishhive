# Future Features

> **Project**: Dishhive
> **Purpose**: Track potential future capabilities for consideration
> **Inspiration**: [Mealie](https://github.com/mealie-recipes/mealie) and community feedback
> **Last Updated**: 2026-06-07

## How to Use This Document

This document captures ideas for future functionality. Entries are not commitments; they are discovery notes to inform roadmap decisions. Each item includes:
- A brief description
- The problem it solves
- Priority assessment (if known)
- Dependencies or prerequisites
- Notes on complexity or risks

When an item is promoted to active development, it should be moved to its own feature document in `docs/features/`.

---

## 1. AI-Assisted Meal Planning

### Description
Use AI to suggest or auto-generate week menus based on constraints, preferences, available ingredients, and frozen meals.

### Problem Solved
Reduces the cognitive load of weekly meal planning by automating suggestions.

### Priority
Medium (extension points exist; implementation deferred)

### Dependencies
- Week Planner foundation
- Recipe Store
- Family Composition
- Freezy integration (for frozen meal awareness)

### Notes
- Extension points are architected via `IPlanningStrategy` interface
- Start with rule-based suggestions before introducing ML/AI
- Consider privacy implications of sending family data to external AI services

---

## 2. Pantry Inventory

### Description
Track ingredients currently available in the pantry to avoid buying duplicates and to prioritize using items before they expire.

### Problem Solved
Reduces food waste and shopping costs by leveraging what's already at home.

### Priority
Medium

### Dependencies
- Recipe Store
- Shopping List Export

### Notes
- Shopping list generation could subtract pantry items
- Expiration date tracking adds complexity
- Barcode scanning could simplify entry (mobile consideration)

---

## 3. Recipe Rating and Reviews

### Description
Allow family members to rate and review planned/cooked dishes.

### Problem Solved
Builds a family-specific preference signal to improve future suggestions.

### Priority
Low

### Dependencies
- Past Dishes and Statistics
- Family Composition

### Notes
- Simple star rating + optional text review
- Aggregate ratings feed into "favorites" statistics
- Could influence future AI planning weights

---

## 4. Multi-Household Support

### Description
Support multiple households with shared recipe access but independent planning.

### Problem Solved
Enables extended families or roommates to share recipes while keeping plans separate.

### Priority
Low

### Dependencies
- All core features stable

### Notes
- Multi-tenancy adds significant complexity
- Consider row-level security or separate schemas
- May not be needed for v1 target audience

---

## 5. Nutritional Tracking

### Description
Calculate and display nutritional information for planned meals and weekly menus.

### Problem Solved
Helps families meet dietary goals and track nutritional balance.

### Priority
Low

### Dependencies
- Recipe Store (nutritional data per ingredient)
- Week Planner

### Notes
- Nutritional database integration is complex
- Could start with macro-level (calories, protein, carbs, fat)
- Ingredient-level accuracy depends on data quality

---

## 6. Meal Planning Templates

### Description
Save recurring weekly patterns as templates for quick reuse.

### Problem Solved
Families often repeat similar weekly patterns; templates reduce repetitive planning.

### Priority
Medium

### Dependencies
- Week Planner

### Notes
- "Copy last week" is the simplest template
- Named templates like "School Week", "Holiday Week", "Budget Week"
- Could include default guest assignments

---

## 7. Grocery Store Integration

### Description
Integrate with online grocery stores for direct order placement.

### Problem Solved
Eliminates manual shopping by pushing lists directly to delivery services.

### Priority
Low

### Dependencies
- Shopping List Export

### Notes
- Store APIs vary widely by region
- Partner integrations require commercial agreements
- Start with export; integrate later if demand exists

---

## 8. Recipe Scaling

### Description
Dynamically scale recipe ingredients when planned servings differ from recipe defaults.

### Problem Solved
Accurate ingredient quantities when cooking for more or fewer people than the recipe assumes.

### Priority
Medium

### Dependencies
- Recipe Store
- Measurement Preferences

### Notes
- Some ingredients don't scale linearly (spices, salt)
- Could flag non-linear ingredients for manual review
- Related to shopping list serving adjustment

---

## 9. Dietary Profile Management

### Description
Define named dietary profiles (vegan, gluten-free, keto) that can be applied to family members or planning constraints.

### Problem Solved
Simplifies constraint management by grouping related dietary rules.

### Priority
Low

### Dependencies
- Family Composition
- Recipe Store (dietary tags on recipes)

### Notes
- Profiles are composable (someone can be vegan + nut-free)
- Recipe compatibility checking adds complexity
- Could auto-filter recipe suggestions

---

## 10. Mobile Application

### Description
Native or PWA mobile app for on-the-go access while shopping or cooking.

### Problem Solved
Improves usability in kitchen and grocery store contexts.

### Priority
Low

### Dependencies
- All core features stable
- Responsive web design foundation

### Notes
- PWA approach reduces maintenance burden
- Offline support for shopping lists and recipes
- Camera support for barcode scanning

---

## 11. Recipe Collaboration

### Description
Allow multiple family members to contribute, edit, and moderate recipes.

### Problem Solved
Distributes recipe entry workload and captures family-specific recipe variations.

### Priority
Low

### Dependencies
- Recipe Store
- Family Composition

### Notes
- Role-based access (contributor, editor, admin)
- Change history for recipes
- Approval workflow for imported recipes

---

## 12. Cooking Mode / Step-by-Step Guide

### Description
A distraction-free, step-by-step recipe view optimized for use while cooking.

### Problem Solved
Makes following recipes easier during active cooking.

### Priority
Low

### Dependencies
- Recipe Store

### Notes
- Large text, minimal UI chrome
- Timer integration for steps with durations
- Voice commands (stretch goal)

---

## 13. Seasonal Planning

### Description
Suggest recipes based on seasonal ingredient availability and pricing.

### Problem Solved
Encourages fresher, cheaper ingredients aligned with seasonal cycles.

### Priority
Low

### Dependencies
- Recipe Store (seasonal tags)
- Week Planner

### Notes
- Requires seasonal data per region
- Could be a simple calendar-based filter
- Ties into future AI planning

---

## 14. Cooking Time Estimation

### Description
Estimate total cooking time per day/week to help balance planning workload.

### Problem Solved
Prevents scheduling too many complex dishes on busy days.

### Priority
Low

### Dependencies
- Recipe Store (prep time, cook time fields)
- Week Planner

### Notes
- Display daily and weekly time summaries
- Color-code days by workload
- Could influence AI planning constraints

---

## 15. Recipe Collections / Playlists

### Description
Create custom collections of recipes (e.g., "Date Night", "Quick Weeknight", "Comfort Food").

### Problem Solved
Organizes recipes beyond simple tagging for easier browsing.

### Priority
Low

### Dependencies
- Recipe Store

### Notes
- Collections can be shared between family members
- Could include community-shared collections later
- Similar to Spotify playlist model

---

## Review Cadence

Revisit this document quarterly to:
1. Promote ready items to feature documents
2. Deprioritize items no longer relevant
3. Add new ideas from user feedback or inspiration sources
4. Update priority assessments based on current roadmap context
