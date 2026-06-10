export interface UserSetting {
  key: string;
  value: string;
  createdAt: string;
  updatedAt: string;
}

export type MeasurementSystem = 'metric' | 'imperial';

/** Setting key for the household's measurement system; metric is the default by absence */
export const MEASUREMENT_SYSTEM_KEY = 'measurementSystem';
