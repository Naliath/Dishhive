# Feature: Localization

Dishhive stores `preferredLanguage` (`en` or `nl`) and `translateImportedRecipes` as
household settings. On first use the browser language selects Dutch when its locale starts
with `nl`; unsupported languages fall back to English. Both settings can be changed during
onboarding or on the settings page.

The Angular runtime loads interface messages from `public/i18n/{language}.json` and updates
the document language. New interface text belongs in these resources rather than in language
branches.

Backend parsing resources live in `Resources/Localization/{language}.json`. Unit aliases,
allergen aliases, and diet aliases are data, not conditionals. All installed source-language
lexicons participate in import normalization because a recipe's source language can differ
from the interface language.

When import translation is enabled and AI is available, the existing background dietary-
facts workflow also translates imported title, description, ingredient names and steps to
the preferred language. Source title/description/ingredient lines/steps remain stored in
their original fields. Translation failures leave extracted recipe text untouched.
