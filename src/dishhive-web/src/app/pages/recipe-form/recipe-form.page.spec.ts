import { ActivatedRoute, Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { describe, expect, it, vi } from 'vitest';
import { RecipesService } from '../../services/recipes.service';
import { PlannedMealsService } from '../../services/planned-meals.service';
import { LanguageService } from '../../services/language.service';
import { RecipeFormPage } from './recipe-form.page';

describe('RecipeFormPage image URL editor', () => {
  function createPage() {
    const snackBar = { open: vi.fn() };
    const language = {
      t: (key: string) => ({
        'recipeForm.invalidImageUrl': 'Enter a valid http(s) image URL',
        'common.dismiss': 'Dismiss'
      })[key] ?? key
    };
    const page = new RecipeFormPage(
      {} as ActivatedRoute,
      {} as Router,
      {} as RecipesService,
      {} as PlannedMealsService,
      snackBar as unknown as MatSnackBar,
      language as unknown as LanguageService
    );
    return { page, snackBar };
  }

  it('prepopulates an existing source and previews the confirmed URL', () => {
    const { page } = createPage();
    page.imageSourceUrl.set('https://images.example/old.jpg');

    page.openImageUrlEditor();
    expect(page.imageUrlDraft()).toBe('https://images.example/old.jpg');

    page.imageUrlDraft.set(' https://images.example/new.jpg ');
    page.confirmImageUrl();

    expect(page.imageSourceUrl()).toBe('https://images.example/new.jpg');
    expect(page.imagePreviewUrl()).toBe('https://images.example/new.jpg');
    expect(page.imageUrlEditorOpen()).toBe(false);
  });

  it('keeps the editor open when the URL is invalid', () => {
    const { page, snackBar } = createPage();
    page.openImageUrlEditor();
    page.imageUrlDraft.set('file:///photo.jpg');

    page.confirmImageUrl();

    expect(page.imageSourceUrl()).toBe('');
    expect(page.imagePreviewUrl()).toBeNull();
    expect(page.imageUrlEditorOpen()).toBe(true);
    expect(snackBar.open).toHaveBeenCalledWith(
      'Enter a valid http(s) image URL', 'Dismiss', { duration: 4000 });
  });

  it('removes the preview and retained source together', () => {
    const { page } = createPage();
    page.imageSourceUrl.set('https://images.example/photo.jpg');
    page.urlImagePreviewUrl.set('https://images.example/photo.jpg');
    page.sourceInfoOpen.set(true);

    page.removeImage();

    expect(page.imagePreviewUrl()).toBeNull();
    expect(page.imageSourceUrl()).toBe('');
    expect(page.sourceInfoOpen()).toBe(false);
    expect(page.imageRemoved()).toBe(true);
  });

  it('offers original ingredient and step comparisons only when values differ', () => {
    const { page } = createPage();
    page.title = 'Translated title';
    page.originalTitle = 'Original title';
    page.description = 'Translated description.';
    page.originalDescription = 'Original description.';
    page.ingredients = [
      { name: 'butter', quantity: 200, unit: 'g', originalText: '200 g boter' },
      { name: 'salt', quantity: 1, unit: 'tsp', originalText: '1 tsp salt' }
    ];
    page.steps = [
      { instruction: 'Melt the butter.', originalInstruction: 'Smelt de boter.' },
      { instruction: 'Serve.', originalInstruction: 'Serve.' }
    ];

    expect(page.originalTitleText()).toBe('Original title');
    expect(page.originalDescriptionText()).toBe('Original description.');
    expect(page.hasOriginalIngredients()).toBe(true);
    expect(page.originalIngredientText(page.ingredients[0])).toBe('200 g boter');
    expect(page.originalIngredientText(page.ingredients[1])).toBeNull();
    expect(page.hasOriginalSteps()).toBe(true);
    expect(page.originalStepInstruction(page.steps[0])).toBe('Smelt de boter.');
    expect(page.originalStepInstruction(page.steps[1])).toBeNull();

    page.title = ' original TITLE ';
    page.description = 'ORIGINAL DESCRIPTION.';
    expect(page.originalTitleText()).toBeNull();
    expect(page.originalDescriptionText()).toBeNull();
  });
});
