import { computed, Injectable, signal } from '@angular/core';

export type RecipeImportSource = 'url' | 'text' | 'photo';
const key = 'dishhive.recipeImport.source.v1';

@Injectable({ providedIn: 'root' })
export class RecipeImportPreference {
  private readonly selected = signal<RecipeImportSource>(this.restore());
  readonly source = this.selected.asReadonly();
  readonly actionLabel = computed(() => ({ url: 'ui.importFromURL', text: 'recipeImport.openText', photo: 'recipeImport.openPhoto' })[this.source()]);

  select(source: RecipeImportSource): void {
    if (!['url', 'text', 'photo'].includes(source)) return;
    this.selected.set(source);
    try { localStorage.setItem(key, source); } catch { /* Storage is optional. */ }
  }

  private restore(): RecipeImportSource {
    try {
      const saved = localStorage.getItem(key);
      return saved === 'text' || saved === 'photo' ? saved : 'url';
    } catch { return 'url'; }
  }
}
