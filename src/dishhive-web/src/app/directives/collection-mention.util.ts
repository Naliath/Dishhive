/**
 * Pure helpers for mention detection in instruction inputs. Two triggers share the
 * same grammar: "#" references a recipe collection (#[Collection Name]) and "@"
 * references an external recipe source (@[Dagelijkse Kost]). The user types the
 * trigger plus a few letters ("#ea" / "@dag"); the autocomplete inserts the complete
 * bracketed token — brackets are never typed by hand (a manually typed "[" is tolerated).
 */

export type MentionTrigger = '#' | '@';

export interface ActiveMention {
  /** Index of the trigger character the mention starts at */
  start: number;
  /** Text between the trigger (and optional '[') and the caret */
  query: string;
  /** Which trigger started this mention */
  trigger: MentionTrigger;
}

/** The in-progress mention the caret is inside of, or null */
export function findActiveMention(text: string, caret: number): ActiveMention | null {
  for (let i = caret - 1; i >= 0; i--) {
    const ch = text[i];
    if (ch === '#' || ch === '@') {
      let query = text.slice(i + 1, caret);
      if (query.startsWith('[')) {
        query = query.slice(1);
      }
      if (query.includes(']')) {
        return null; // a completed token, not an in-progress mention
      }
      return { start: i, query, trigger: ch };
    }
    if (ch === ']' || ch === '\n' || ch === '\r') {
      return null;
    }
  }
  return null;
}

/** Replaces the in-progress mention with the full token, returning text and caret */
export function applyMention(
  text: string, caret: number, start: number, trigger: MentionTrigger, name: string
): { text: string; caret: number } {
  const token = `${trigger}[${name}] `;
  return {
    text: text.slice(0, start) + token + text.slice(caret),
    caret: start + token.length
  };
}
