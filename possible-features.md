# Possible Features

Future feature ideas for Dishhive. Functional inspiration drawn from the
[Mealie](https://github.com/mealie-recipes/mealie) project — used for capability discovery,
[KitchenOwn](https://github.com/TomBursch/kitchenowl) possible insipiration
not copied blindly. Items graduate from this list into `docs/features/*.md` when they get planned.

## Small Scope Features

### Recipe scaling in the UI
Servings stepper on the recipe detail page that rescales displayed ingredient quantities
(model already supports it: quantities + servings).

### Ingredient canonicalization
Map spelling variants ("ui"/"uien") to one ingredient for cleaner shopping aggregation and stats. Manage these mappings in the settings so they can be manually tweaked

## Medium Scope Features

### Persisted shopping list with check-off
Promote the computed shopping list to a persisted, checkable list with manually added extra
items, shared across devices. 

### Cooking mode
Full-screen step-by-step view with kept-awake screen and step timers, like Dagelijkse Kost's
"kookmodus". (Mealie: recipe step view with timers.)

### Nutrition information
Per-recipe nutrition (imported when sources provide it; manual otherwise). (Mealie: nutrition
fields per recipe.)

### Multi-course AI suggestions
AI week-plan suggestions currently only ever propose a dinner main (`Course.Main`) — the
domain model already supports `Appetizer`/`Side`/`Dessert` (used by manual planning via the
meal-slot dialog) but the AI path is blind to it. Scope, when planned:
- JSON contract gains a `course` field per suggestion (default `main`); `PostProcess` groups
  by `(Date, Course)` instead of just capping at 3 dishes/day per date.
- Trigger: **free-text only** — no new mention syntax. "Add a dessert for Friday" in the
  existing Instructions/VagueInstruction field, interpreted by a new system-prompt rule
  ("if a course is requested, set course in the JSON reply"). Decided against a dedicated
  `+[Dessert]`-style token: this is an occasional ask, and the system-prompt cost of
  supporting it is fixed/negligible (~30-50 tokens, doesn't scale with the recipe library)
  regardless of how often it's used, so there's no efficiency reason to gate it behind a
  special trigger the way `#[Collection]`/`@[Source]` gate their (much larger, per-request)
  context blocks.
- **Needs a result-screen rework**: `suggestion-review-dialog` currently lists suggestions
  flatly; would need to group rows under course headers per day. The accept flow
  (`week-planner.page.ts`) hardcodes `course: Course.Main` when creating meals from accepted
  suggestions — needs to read `suggestion.course` instead.

## Large Scope Features

### Meal plan rules & automation
Recurring rules ("Friday = pizza day", "max 2× meat per week") feeding the suggestion engine.

### External integrations
Calendar export (iCal) of the week menu; grocery-store or Home Assistant integrations.
(Mealie: API tokens + integration ecosystem.)
