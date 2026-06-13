/**
 * Pure helpers for #[Collection Name] mention detection in instruction inputs.
 * The user types "#" plus a few letters ("#ea"); the autocomplete inserts the
 * complete bracketed token ("#[Easy Weekday Dishes] ") — brackets are never typed
 * by hand (a manually typed "[" after the "#" is tolerated).
 */

export interface ActiveMention {
  /** Index of the '#' character the mention starts at */
  start: number;
  /** Text between the '#' (and optional '[') and the caret */
  query: string;
}

/** The in-progress mention the caret is inside of, or null */
export function findActiveMention(text: string, caret: number): ActiveMention | null {
  for (let i = caret - 1; i >= 0; i--) {
    const ch = text[i];
    if (ch === '#') {
      let query = text.slice(i + 1, caret);
      if (query.startsWith('[')) {
        query = query.slice(1);
      }
      if (query.includes(']')) {
        return null; // a completed #[…] token, not an in-progress mention
      }
      return { start: i, query };
    }
    if (ch === ']' || ch === '\n' || ch === '\r') {
      return null;
    }
  }
  return null;
}

/** Replaces the in-progress mention with the full token, returning text and caret */
export function applyMention(
  text: string, caret: number, start: number, collectionName: string
): { text: string; caret: number } {
  const token = `#[${collectionName}] `;
  return {
    text: text.slice(0, start) + token + text.slice(caret),
    caret: start + token.length
  };
}
