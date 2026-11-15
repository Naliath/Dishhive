import { beforeEach, describe, expect, it, vi } from 'vitest';
import { RecipeImportPreference } from './recipe-import-preference.service';

describe('RecipeImportPreference', () => {
  beforeEach(() => localStorage.removeItem('dishhive.recipeImport.source.v1'));
  it('defaults to URL and restores the last selection', () => {
    const preference = new RecipeImportPreference();
    expect(preference.source()).toBe('url');
    preference.select('photo');
    expect(new RecipeImportPreference().source()).toBe('photo');
    preference.select('text');
    expect(new RecipeImportPreference().source()).toBe('text');
  });
  it('ignores corrupt storage', () => {
    localStorage.setItem('dishhive.recipeImport.source.v1', 'unrecognized');
    expect(new RecipeImportPreference().source()).toBe('url');
  });
  it('still works when storage is blocked', () => {
    const read = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('blocked'); });
    const write = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('blocked'); });
    const preference = new RecipeImportPreference(); preference.select('text');
    expect(preference.source()).toBe('text'); read.mockRestore(); write.mockRestore();
  });
});
