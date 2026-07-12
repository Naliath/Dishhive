export interface UserSetting {
  key: string;
  value: string;
  createdAt: string;
  updatedAt: string;
}

export type MeasurementSystem = 'metric' | 'imperial';

/** Setting key for the household's measurement system; metric is the default by absence */
export const MEASUREMENT_SYSTEM_KEY = 'measurementSystem';

export type FirstDayOfWeek = 'monday' | 'sunday';

/** Setting key for the week's first day; Monday is the default by absence */
export const FIRST_DAY_OF_WEEK_KEY = 'firstDayOfWeek';

export type SupportedLanguage = 'en' | 'nl';
export const SUPPORTED_LANGUAGES: ReadonlyArray<{ code: SupportedLanguage; labelKey: string }> = [
  { code: 'en', labelKey: 'English' },
  { code: 'nl', labelKey: 'Dutch' }
];
export const PREFERRED_LANGUAGE_KEY = 'preferredLanguage';
export const TRANSLATE_IMPORTED_RECIPES_KEY = 'translateImportedRecipes';

/** The editable AI system prompt and its context (GET/PUT/DELETE /api/settings/ai-prompt) */
export interface AiPromptSettings {
  /** The effective editable section (the user's override, or the default) */
  editablePrompt: string;
  /** The shipped default for the editable section (reset target) */
  defaultPrompt: string;
  /** The machinery always appended after the editable section — read-only */
  protectedPrompt: string;
  isCustomized: boolean;
  /** The shipped default improved since the user customized (they're on an old fork) */
  defaultChangedSinceCustomized: boolean;
}
