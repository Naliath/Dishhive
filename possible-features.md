# Possible Features

Future feature ideas for Dishhive. Functional inspiration drawn from the
[Mealie](https://github.com/mealie-recipes/mealie) project — used for capability discovery,
[KitchenOwn](https://github.com/TomBursch/kitchenowl) possible insipiration
not copied blindly. Items graduate from this list into `docs/features/*.md` when they get planned.


## Fixes
- Too many freezers dinners are being planned there should be a limited amount unless specified otherwise (1 to 2 a week).
- Planning is not always matching the local recipes correctly. The title is sometimes the original dutch not the current title (is that passed? Application is set in EN). The returned dish is then not correctly linked to the local recipe because the name does not always track 100%
- Add a warning if it is not possible to do a web search (offline)

## Small Scope Features

### Quick attendance setup for generated plans
Add a fast way to choose the attending family members before generating week suggestions,
with day-level adjustments that do not turn the common household setup into a long form.
Use existing concrete planned meals and vague meal ideas to infer the initial attendance
settings for each day; fall back to the normal non-guest household defaults where the plan
does not already provide a useful signal.

### Ingredient canonicalization
Map spelling variants ("ui"/"uien") to one ingredient for cleaner shopping aggregation and stats. Manage these mappings in the settings so they can be manually tweaked

### Reorde planned meals
Allow switching planned meals of meals that are being planned in an easy fashion. Perhaps drag and drop them to another day. This allows the user to quickly match planning to reality.

## Medium Scope Features

### Refactor the client side API using NSwag
The following error should be fixed "NSwag now generates contract classes and enums only. Full client generation remains disabled because unrelated recipe endpoints currently have duplicate OpenAPI operation names." So that there single source of truth regarding the dto's that flow from front-end to back-end and back.

### Persisted shopping list with check-off
Promote the computed shopping list to a persisted, checkable list with manually added extra
items, shared across devices. 

### Cooking mode
Full-screen step-by-step view with kept-awake screen and step timers, like Dagelijkse Kost's
"kookmodus". (Mealie: recipe step view with timers.)

### Nutrition information
Per-recipe nutrition (imported when sources provide it; manual otherwise). (Mealie: nutrition
fields per recipe.)

## Large Scope Features

### Meal plan rules & automation
Recurring rules ("Friday = pizza day", "max 2× meat per week") feeding the suggestion engine.

### External integrations
Calendar export (iCal) of the week menu; grocery-store or Home Assistant integrations.
(Mealie: API tokens + integration ecosystem.)

### Browser extension 
Can be used as a conduit for automated adding to cart in a shop, adding recipes to dishhive directly from a recipe page, etc
