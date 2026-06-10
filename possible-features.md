# Possible Features

Future feature ideas for Dishhive. Functional inspiration drawn from the
[Mealie](https://github.com/mealie-recipes/mealie) project — used for capability discovery,
not copied blindly. Items graduate from this list into `docs/features/*.md` when they get planned.

## Needed soon

### AI-assisted week planning
Implement a real provider behind the existing `IMealSuggestionService` seam: propose a week
plan from family constraints/allergies, favorites, dish history (variety), vague instructions
already on the plan, and expiring Freezy items. Likely an LLM-backed provider with a
deterministic rules fallback. The seam exists; do not bolt AI on anywhere else.

### Meal eaten / rating feedback
Mark planned meals as actually cooked/eaten and let members rate them. Feeds statistics
("planned 9×, loved 8×") and future AI suggestions.

## Small Scope Features

### Generic schema.org import fallback
Register a catch-all `GenericSchemaOrgProvider` so any site with valid Recipe JSON-LD can be
imported without a dedicated provider. (Mealie's URL import works this way.)

### Copy previous week
One-click duplicate of a past week's plan into the current week as a starting point.

### Recipe scaling in the UI
Servings stepper on the recipe detail page that rescales displayed ingredient quantities
(model already supports it: quantities + servings).

### Structured allergy/constraint tags
Replace free-text allergies/constraints with reusable tags once real usage shows recurring
values; enables hard filtering in the planner and AI suggestions.

### Ingredient canonicalization
Map spelling variants ("ui"/"uien") to one ingredient for cleaner shopping aggregation and stats. Manage these mappings in the settings so they can be manually tweaked

## Medium Scope Features

### Persisted shopping list with check-off
Promote the computed shopping list to a persisted, checkable list with manually added extra
items, shared across devices. 

### Recipe organization: tags, categories, cookbooks
Filterable tags/categories and curated "cookbooks" (saved filters) as the library grows.
(Mealie: categories, tags, tools, cookbooks.)

### Cooking mode
Full-screen step-by-step view with kept-awake screen and step timers, like Dagelijkse Kost's
"kookmodus". (Mealie: recipe step view with timers.)

### PWA support
Installable app + offline read access to the week plan and shopping list, following Freezy's
service-worker setup (deliberate initial deviation, see infrastructure plan §2).

### Freezy write-back
When a freezer-sourced meal is marked eaten, decrement/consume the item in Freezy via its
existing API (Dishhive→Freezy direction preserved).

### Nutrition information
Per-recipe nutrition (imported when sources provide it; manual otherwise). (Mealie: nutrition
fields per recipe.)

### Recipes scraping generalization
Perhaps use one of these instead of a pure own implementation https://github.com/hhursev/recipe-scrapers, https://github.com/simfoley/RecipeScraper

### Recipes import/export
Figure out if there is an well used format and adopt that for an import/export function (perhaps from the settings page)

## Large Scope Features

### Meal plan rules & automation
Recurring rules ("Friday = pizza day", "max 2× meat per week") feeding the suggestion engine.
(Mealie: mealplan rules with random recipes by category.)

### Pantry / stock awareness
Track staples at home and subtract them from shopping lists; integrate Freezy stock as one of
several storage locations.

### External integrations
Calendar export (iCal) of the week menu; grocery-store or Home Assistant integrations.
(Mealie: API tokens + integration ecosystem.)
