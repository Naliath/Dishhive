import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of, Subject } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { RecipeImportComponent } from './recipe-import';
import { RecipesService } from '../../services/recipes.service';
import { LanguageService } from '../../services/language.service';
import { Recipe } from '../../models/recipe.model';
import { RecipeImportCapabilitiesDto } from '../../api/generated/dishhive-api.client';

describe('RecipeImportComponent', () => {
  let request: Subject<Recipe>;
  let vision: Subject<RecipeImportCapabilitiesDto>;
  const service = {
    getImportCapabilities: vi.fn(() => of({ textAvailable: true, photoState: 'available', maxTextCharacters: 30000, maxImageBytes: 15728640 })),
    importRecipeText: vi.fn(() => request), importRecipe: vi.fn(() => request), importRecipeImage: vi.fn(() => request),
    checkImportImageCapability: vi.fn(() => vision)
  };
  beforeEach(async () => {
    localStorage.removeItem('dishhive.recipeImport.source.v1'); request = new Subject<Recipe>(); vision = new Subject<RecipeImportCapabilitiesDto>();
    vi.clearAllMocks();
    await TestBed.configureTestingModule({ imports: [RecipeImportComponent], providers: [
      { provide: RecipesService, useValue: service },
      { provide: LanguageService, useValue: { language: signal('en'), t: (key: string) => key } }
    ] }).compileComponents();
  });
  function open() {
    const fixture = TestBed.createComponent(RecipeImportComponent);
    fixture.componentRef.setInput('visible', true); fixture.detectChanges();
    return fixture;
  }
  it('retains text and selection after failure and prevents duplicate requests', () => {
    const fixture = open(); const component = fixture.componentInstance;
    component.selectSource('text'); component.text = '200 g flour. Mix.';
    component.import(); component.import();
    expect(service.importRecipeText).toHaveBeenCalledTimes(1);
    expect(service.importRecipeText).toHaveBeenCalledWith(component.text);
    request.error({ error: { title: 'No recipe', detail: 'Include instructions.' } });
    expect(component.text).toBe('200 g flour. Mix.');
    expect(component.preference.source()).toBe('text'); expect(component.error()).toContain('Include instructions.');
    fixture.componentRef.setInput('visible', false); fixture.detectChanges();
    fixture.componentRef.setInput('visible', true); fixture.detectChanges();
    expect(component.text).toBe('200 g flour. Mix.');
  });
  it('preserves the selected mode while AI is unavailable', () => {
    const component = open().componentInstance; component.selectSource('text'); component.text = 'Recipe';
    component.capabilities.set({ textAvailable: false, photoState: 'unavailable', maxTextCharacters: 30000, maxImageBytes: 15728640 });
    expect(component.canImport()).toBe(false); expect(component.preference.source()).toBe('text');
    component.selectSource('url'); component.url = 'https://example.com/recipe'; expect(component.canImport()).toBe(true);
  });
  it('cancels without clearing the draft or accepting a stale completion', () => {
    const component = open().componentInstance; component.selectSource('text'); component.text = 'Recipe';
    const emit = vi.spyOn(component.completed, 'emit'); component.import(); component.cancel();
    request.next({ title: 'Stale' } as Recipe);
    expect(component.text).toBe('Recipe'); expect(emit).not.toHaveBeenCalled(); expect(component.importing()).toBe(false);
  });
  it('finishes an image check after closing and reopening the form', () => {
    const fixture = open(); const component = fixture.componentInstance;
    component.capabilities.set({ textAvailable: true, photoState: 'notChecked', maxTextCharacters: 30000, maxImageBytes: 15728640 });
    component.selectSource('photo'); expect(component.checking()).toBe(true);
    fixture.componentRef.setInput('visible', false); fixture.detectChanges();
    fixture.componentRef.setInput('visible', true); fixture.detectChanges();
    vision.next({ textAvailable: true, photoState: 'available', maxTextCharacters: 30000, maxImageBytes: 15728640 });
    expect(component.checking()).toBe(false);
    expect(component.capabilities()?.photoState).toBe('available');
  });
});
