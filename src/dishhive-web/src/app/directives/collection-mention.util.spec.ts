import { applyMention, findActiveMention } from './collection-mention.util';

describe('findActiveMention', () => {
  it('finds a bare # with a partial query', () => {
    const text = 'friday something from #ea';
    expect(findActiveMention(text, text.length)).toEqual({ start: 22, query: 'ea', trigger: '#' });
  });

  it('finds a bare @ source mention', () => {
    const text = 'vegetarian from @dag';
    expect(findActiveMention(text, text.length)).toEqual({ start: 16, query: 'dag', trigger: '@' });
  });

  it('allows spaces in the query (multi-word names)', () => {
    const text = 'from #easy week';
    expect(findActiveMention(text, text.length)).toEqual({ start: 5, query: 'easy week', trigger: '#' });
  });

  it('tolerates a manually typed opening bracket', () => {
    const text = 'from #[ea';
    expect(findActiveMention(text, text.length)).toEqual({ start: 5, query: 'ea', trigger: '#' });
  });

  it('returns an empty query right after the trigger', () => {
    expect(findActiveMention('pick #', 6)).toEqual({ start: 5, query: '', trigger: '#' });
    expect(findActiveMention('pick @', 6)).toEqual({ start: 5, query: '', trigger: '@' });
  });

  it('ignores completed tokens', () => {
    const text = 'from #[Easy Weekday Dishes] please';
    expect(findActiveMention(text, text.length)).toBeNull();
  });

  it('detects a second mention after a completed token', () => {
    const text = '#[Comfort Food] or @co';
    expect(findActiveMention(text, text.length)).toEqual({ start: 19, query: 'co', trigger: '@' });
  });

  it('returns the trigger closest to the caret', () => {
    const text = 'from #[Dishes] and @so';
    expect(findActiveMention(text, text.length)).toEqual({ start: 19, query: 'so', trigger: '@' });
  });

  it('returns null when the caret is before the trigger', () => {
    expect(findActiveMention('pick #ea', 3)).toBeNull();
  });

  it('returns null without any trigger', () => {
    expect(findActiveMention('something quick', 15)).toBeNull();
  });
});

describe('applyMention', () => {
  it('replaces the partial collection mention with the full token and trailing space', () => {
    const text = 'friday from #ea please';
    const caret = 'friday from #ea'.length;
    const result = applyMention(text, caret, 12, '#', 'Easy Weekday Dishes');

    expect(result.text).toBe('friday from #[Easy Weekday Dishes]  please');
    expect(result.caret).toBe('friday from #[Easy Weekday Dishes] '.length);
  });

  it('replaces a source mention with an @ token', () => {
    const result = applyMention('from @dag', 9, 5, '@', 'Dagelijkse Kost');

    expect(result.text).toBe('from @[Dagelijkse Kost] ');
    expect(result.caret).toBe(result.text.length);
  });

  it('works at the end of the text', () => {
    const result = applyMention('pick #co', 8, 5, '#', 'Comfort Food');

    expect(result.text).toBe('pick #[Comfort Food] ');
    expect(result.caret).toBe(result.text.length);
  });
});
