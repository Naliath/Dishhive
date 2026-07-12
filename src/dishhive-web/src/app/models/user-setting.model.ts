export interface UserSetting {
  key: string;
  value: string;
  createdAt: string;
  updatedAt: string;
}

export { FirstDayOfWeek, MeasurementSystem } from '../api/generated/dishhive-api.client';

/** Language codes are data-driven by the API's embedded translation resources. */
export type SupportedLanguage = string;

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
