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

### Reorde planned meals
Allow switching planned meals of meals that are being planned in an easy fashion. Perhaps drag and drop them to another day. This allows the user to quickly match planning to reality.

### Add ID matching to freezy recepies comming from the AI
Currently it is possible for a recipe comming from the AI to reference a freezy dish but it not be marked as such.

## Medium Scope Features

### Preferred language selection
Support multiple inteface languages based on industry standard multi-lingual support practices. Start with Dutch and English (currently the only language). Add a global setting to the settings page and also allow it to be set during onboarding (default to the preffered browser language and when not an available language set to english).

There should be a translation file for things like interface elements, ingriedient normalization lists, alergens, etc.

During recipe import there is an AI step to to determine the alergens and classifications. During that step we should add a translation to the user set language (governed by a toggle in the settings). Further ingiedient normalization might be warrented as well. The full steps etc should have an original just like we have for the ingriedients so that errors can be compared to the original.

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
