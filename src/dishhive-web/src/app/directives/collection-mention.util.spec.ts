import { applyMention, findActiveMention } from './collection-mention.util';

describe('findActiveMention', () => {
  it('finds a bare # with a partial query', () => {
    const text = 'friday something from #ea';
    expect(findActiveMention(text, text.length)).toEqual({ start: 22, query: 'ea' });
  });

  it('allows spaces in the query (multi-word names)', () => {
    const text = 'from #easy week';
    expect(findActiveMention(text, text.length)).toEqual({ start: 5, query: 'easy week' });
  });

  it('tolerates a manually typed opening bracket', () => {
    const text = 'from #[ea';
    expect(findActiveMention(text, text.length)).toEqual({ start: 5, query: 'ea' });
  });

  it('returns an empty query right after the #', () => {
    expect(findActiveMention('pick #', 6)).toEqual({ start: 5, query: '' });
  });

  it('ignores completed tokens', () => {
    const text = 'from #[Easy Weekday Dishes] please';
    expect(findActiveMention(text, text.length)).toBeNull();
  });

  it('detects a second mention after a completed token', () => {
    const text = '#[Comfort Food] or #co';
    expect(findActiveMention(text, text.length)).toEqual({ start: 19, query: 'co' });
  });

  it('returns null when the caret is before the #', () => {
    expect(findActiveMention('pick #ea', 3)).toBeNull();
  });

  it('returns null without any #', () => {
    expect(findActiveMention('something quick', 15)).toBeNull();
  });
});

describe('applyMention', () => {
  it('replaces the partial mention with the full token and trailing space', () => {
    const text = 'friday from #ea please';
    const caret = 'friday from #ea'.length;
    const result = applyMention(text, caret, 12, 'Easy Weekday Dishes');

    expect(result.text).toBe('friday from #[Easy Weekday Dishes]  please');
    expect(result.caret).toBe('friday from #[Easy Weekday Dishes] '.length);
  });

  it('works at the end of the text', () => {
    const result = applyMention('pick #co', 8, 5, 'Comfort Food');

    expect(result.text).toBe('pick #[Comfort Food] ');
    expect(result.caret).toBe(result.text.length);
  });
});
