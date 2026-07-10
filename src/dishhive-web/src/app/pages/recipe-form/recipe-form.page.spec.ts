import { ActivatedRoute, Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { describe, expect, it, vi } from 'vitest';
import { RecipesService } from '../../services/recipes.service';
import { PlannedMealsService } from '../../services/planned-meals.service';
import { RecipeFormPage } from './recipe-form.page';

describe('RecipeFormPage image URL editor', () => {
  function createPage() {
    const snackBar = { open: vi.fn() };
    const page = new RecipeFormPage(
      {} as ActivatedRoute,
      {} as Router,
      {} as RecipesService,
      {} as PlannedMealsService,
      snackBar as unknown as MatSnackBar
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
});
