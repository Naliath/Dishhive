export type UserSetting = import('../api/generated/dishhive-api.client').UserSettingDto;

export { FirstDayOfWeek, MeasurementSystem } from '../api/generated/dishhive-api.client';

/** Language codes are data-driven by the API's embedded translation resources. */
export type SupportedLanguage = string;

/** The editable AI system prompt and its context (GET/PUT/DELETE /api/settings/ai-prompt) */
export type AiPromptSettings = import('../api/generated/dishhive-api.client').AiPromptDto;
