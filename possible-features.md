# Possible Features

> Not committed. Inspired in part by [Mealie](https://github.com/mealie-recipes/mealie). Use this list when prioritizing future work.

## Small scope

### Calendar export (iCal)
Export the week plan as an `.ics` feed so it shows up in family members' calendars.

### Print-friendly weekly view
A printable A4/Letter layout of the week's meal plan and shopping list.

### Recipe ratings & quick notes
Per-family-member 1–5 star rating + free-form note attached to a recipe.

### Multiple meal-type configuration
Configurable meal slots per day (e.g., toggling Breakfast / Snack on/off, or adding a "Late dinner" slot).

### Recipe duplication / fork
Duplicate an existing recipe to start editing a personal variant without losing the original.

## Medium scope

### Pantry / staples tracker (separate from Freezy)
Track non-frozen pantry staples (rice, oil, …) so the shopping list can subtract what's already at home.

### Meal photo gallery
Upload your own picture after cooking; store with the dish history entry.

### Smart deduplication of ingredients
Lemma/synonym-based ingredient matching for shopping list generation (e.g. "spring onion" ↔ "scallion").

### More recipe import sources
Pluggable providers for additional sites (NYT Cooking, AllRecipes, Mealie export, etc.) — the infrastructure exists, only new providers are needed.

### Push-back to Freezy
When a planned Freezy meal is cooked, mark the corresponding item as consumed in Freezy via its API. Requires a small Freezy-side endpoint (or reuse of existing consumption events).

### Multi-household / sharing
Mirror Freezy's planned multi-user model: optional auth via magic link, household membership, context switching. See Freezy `docs/plans/USER_SYSTEM_PLAN.md` for the reference design.

## Large scope

### AI-assisted weekly planning
Use the existing `IMealSuggestionStrategy` seam to plug an LLM-based or rules-based planner that:
- respects family dietary constraints,
- prefers seasonal ingredients,
- avoids dishes planned recently (via history),
- balances effort across the week,
- can fill "vague intent" slots with concrete suggestions.

Privacy-friendly variants: local-only LLM via Ollama; remote LLM with explicit user opt-in.

### Nutrition tracking
Per-recipe macro/micronutrient computation based on ingredients, with weekly summaries per family member.

### Mobile companion / PWA
Offline-capable mobile experience for in-store shopping-list use, in the same spirit as Freezy.

### Recipe collaboration
Share a recipe (read-only or fork-able) with other Dishhive instances or via a public link.

### Mealie import/export
First-class compatibility with the Mealie data format so users can migrate in or out without losing recipes.

### Voice-driven planning ("add fish on Wednesday")
Speech-to-text → intent parser → planner update.
