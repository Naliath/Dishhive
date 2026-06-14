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

### Rework the recipies grouping solution
Current cookbook implementation is lacking basing is only on a filter, this allows very little customization or requires long filter names to group different items. More ideas required

### Persisted shopping list with check-off
Promote the computed shopping list to a persisted, checkable list with manually added extra
items, shared across devices. 

### Cooking mode
Full-screen step-by-step view with kept-awake screen and step timers, like Dagelijkse Kost's
"kookmodus". (Mealie: recipe step view with timers.)

### Freezy write-back
When a freezer-sourced meal is marked eaten, decrement/consume the item in Freezy via its
existing API (Dishhive→Freezy direction preserved).

### Nutrition information
Per-recipe nutrition (imported when sources provide it; manual otherwise). (Mealie: nutrition
fields per recipe.)

## Large Scope Features

### Meal plan rules & automation
Recurring rules ("Friday = pizza day", "max 2× meat per week") feeding the suggestion engine.

### Pantry / stock awareness
Track staples at home and subtract them from shopping lists; integrate Freezy stock as one of several storage locations.

### External integrations
Calendar export (iCal) of the week menu; grocery-store or Home Assistant integrations.
(Mealie: API tokens + integration ecosystem.)
