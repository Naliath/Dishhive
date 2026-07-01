/** A recipe source the app knows about, for the @[Source] mention picker */
export interface RecipeSource {
  /** Friendly display name (e.g. "Dagelijkse Kost", or the host for ad-hoc sources) */
  name: string;
  /** Website host, e.g. "dagelijksekost.vrt.be" */
  host: string;
}
