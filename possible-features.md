# Dishhive — Possible Future Features

**Created:** April 19, 2026  
**Style:** Living document — add ideas freely, review during planning

---

This document captures potential future features for Dishhive.
Items are not prioritized unless marked. Use this as a backlog for discussion.

*Inspired by the [Mealie project](https://github.com/mealie-recipes/mealie) as a reference for household recipe management capabilities. All items are evaluated for Dishhive's specific context — not copied blindly.*

---

## Short term
- Remove al alert style pop-ups and replace with dialogs


## 🍽️ Meal Planning Enhancements

- **Multi-week planning** — Plan more than one week ahead; link recurring weeks.
- **Meal plan templates** — Save a week plan as a named template to reuse.
- **Breakfast & lunch planning** — Currently only dinner is the primary focus; expand to full-day planning.
- **Meal rotation reminders** — Warn when the same dish was planned recently.
- **Nutritional targets** — Track calories/macros per day (requires nutritional data on recipes).
- **Calendar sync** — Export week plan to Google Calendar / iCal.
- **AI-assisted meal planning** — Suggest meals based on family preferences, recent history, and available frozen items. Extension point already in architecture.
- **Seasonal suggestions** — Filter/prefer recipes by season.
- **Budget-aware planning** — Associate a cost estimate per recipe; flag when a week plan exceeds a threshold.

---

## 📖 Recipe Enhancements

- **Recipe scaling** — Automatically scale ingredient quantities for a different number of servings.
- **Recipe versioning** — Track edits to a recipe over time; restore previous versions.
- **Recipe collections / cookbooks** — Group recipes into user-defined collections.
- **Recipe rating & notes** — Rate a cooked recipe and add personal notes.
- **Recipe duplication** — Copy a recipe as a starting point for a variation.
- **Nutritional information** — Attach nutritional data per recipe (calories, protein, etc.); source from Open Food Facts or manual entry.
- **Recipe difficulty level** — Tag as easy / medium / advanced.
- **Cooking method tags** — Oven, stovetop, slow cooker, air fryer, etc.
- **Prep/cook time filtering** — Filter recipes by total time available.
- **Recipe image upload** — Allow uploading an image instead of requiring a URL.

---

## 🌐 Recipe Import Sources

- **Dagelijkse Kost** — ✅ Already supported (Phase 1)
- **Njam.tv** — Belgian cooking TV show website
- **15gram.be** — Belgian food blog
- **Recepten.be** — Belgian recipe aggregator
- **Allerhande (Albert Heijn)** — Dutch supermarket recipe site
- **Jumbo Recepten** — Dutch supermarket recipe site
- **BBC Good Food** — English-language recipes
- **Open universal schema.org importer** — Accept any site that provides schema.org/Recipe JSON-LD

---

## 🛒 Shopping List Enhancements

- **Ingredient deduplication with NLP** — Merge "1 ui" and "2 uien" intelligently.
- **Category-based grouping** — Group shopping items by store section (produce, dairy, meat, etc.).
- **Share via messaging apps** — Use the Web Share API to share the shopping list.
- **PDF/print export** — Export the shopping list as a formatted PDF.
- **Persistent saved lists** — Save a shopping list to review later.
- **Mark items as "in stock"** — Skip purchasing items already at home.
- **Pantry inventory** — Track items on hand to automatically exclude from shopping lists.

---

## 👨‍👩‍👧 Family & Household

- **Authentication / family units** — Multi-device support via passwordless magic-link auth (see Freezy's `USER_SYSTEM_PLAN.md` for a reference design).
- **Household data sharing** — Sync data across devices within a household.
- **Guest meal planning** — Dedicated view for planning meals when guests are visiting.
- **Children's preferences** — Special flags for age-appropriate meals.
- **Allergy warning system** — Automatically warn when planning a meal that conflicts with a family member's allergy.

---

## 🔗 Freezy Integration Enhancements

- **Mark frozen item as consumed** — Write-back to Freezy when a frozen item is used in a planned meal.
- **Expiration-aware planning** — Prioritize frozen items close to expiration when planning.
- **Real-time stock check** — Show current freezer stock inline in the week planner.
- **Shared authentication** — If both apps adopt the family unit auth system, share the session.

---

## 📊 Statistics & Insights

- **Dish rotation analysis** — Visualize how varied the menu has been over the past N weeks.
- **Per-member meal satisfaction** — Track which dishes each family member rated positively.
- **Cuisine type breakdown** — Tag recipes by cuisine; chart which cuisines are most cooked.
- **Seasonal cooking patterns** — See which recipes appear more in winter vs summer.

---

## ⚙️ Technical & Infrastructure

- **PWA push notifications** — Remind the family when the week hasn't been planned yet.
- **Offline support** — Cache the current week plan for offline viewing (PWA service worker).
- **API versioning** — Add `/api/v2/` versioning when breaking changes are needed.
- **Multi-language UI** — Dutch/English language toggle (Dagelijkse Kost source is Dutch).
- **Dark mode toggle** — Manual override on top of system preference.
- **Home screen widget** — PWA shortcut / widget showing today's dinner.
- **Webhook integration** — Notify external systems (e.g., home automation) when a meal is planned.
- **Self-hosted backup** — Scheduled export of all data to JSON/CSV for backup purposes.

---

## 🤖 AI / Smart Features (Future)

- **Ingredient-based recipe suggestions** — "What can I cook with what's in the freezer?"
- **Automatic shopping list from spoken input** — Voice command integration.
- **Recipe summarization** — Summarize long recipes into a quick overview.
- **Allergen detection** — Parse ingredient lists and automatically flag allergens.
- **Meal plan explanation** — AI explains why it suggested a particular meal.
