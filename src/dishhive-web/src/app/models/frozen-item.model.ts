/** Read model of a Freezy frozen item (see docs/features/freezy-integration.md) */
export interface FrozenItem {
  id: string;
  name: string;
  quantity: number;
  unit?: string;
  expirationDate?: string;
  notes?: string;
}

export interface FreezerSuggestions {
  /** False when the Freezy integration is not configured */
  enabled: boolean;
  items: FrozenItem[];
}
