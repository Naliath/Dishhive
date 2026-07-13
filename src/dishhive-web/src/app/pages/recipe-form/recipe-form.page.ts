import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, computed, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { COMMA, ENTER } from '@angular/cdk/keycodes';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipInputEvent, MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ClassPickerComponent } from '../../components/class-picker/class-picker';
import { CookingLoaderComponent } from '../../components/cooking-loader/cooking-loader';
import { RecipesService } from '../../services/recipes.service';
import { PlannedMealsService } from '../../services/planned-meals.service';
import { CreateRecipe, DietaryFactsStatus, Recipe } from '../../models/recipe.model';
import { Observable, map, of, switchMap, tap } from 'rxjs';
import { TranslatePipe } from '../../services/language.service';

interface IngredientRow {
  name: string;
  quantity: number | null;
  unit: string;
  originalText?: string;
}

interface StepRow {
  instruction: string;
  originalInstruction?: string;
}

/**
 * Manual recipe create/edit form. Ingredients and steps are replaced wholesale on
 * save (see docs/features/recipe-store.md).
 */
@Component({
  selector: 'app-recipe-form-page',
  standalone: true,
  imports: [
    ClassPickerComponent,
    CookingLoaderComponent,
    RouterLink,
    FormsModule,
    MatAutocompleteModule,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSlideToggleModule,
    MatSnackBarModule,
    MatTooltipModule,
    TranslatePipe
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './recipe-form.page.html',
  styleUrl: './recipe-form.page.scss'
})
export class RecipeFormPage implements OnInit, OnDestroy {
  private static readonly maxImageBytes = 15 * 1024 * 1024;

  readonly separatorKeys = [ENTER, COMMA] as const;

  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly editingId = signal<string | null>(null);
  readonly cameraCaptureAvailable = signal(false);

  title = '';
  description = '';
  originalTitle?: string;
  originalDescription?: string;
  contentLanguage?: string;
  servings = 4;
  prepTimeMinutes: number | null = null;
  cookTimeMinutes: number | null = null;
  category = '';
  keywords = '';
  readonly imageSourceUrl = signal('');
  readonly imageUrlDraft = signal('');
  readonly imageUrlEditorOpen = signal(false);
  readonly urlImagePreviewUrl = signal<string | null>(null);
  readonly sourceInfoOpen = signal(false);
  readonly currentImageUrl = signal<string | null>(null);
  readonly selectedImagePreviewUrl = signal<string | null>(null);
  readonly imageRemoved = signal(false);
  readonly imagePreviewUrl = computed(() =>
    this.selectedImagePreviewUrl()
      ?? this.urlImagePreviewUrl()
      ?? (this.imageRemoved() ? null : this.currentImageUrl()));
  private selectedImageFile: File | null = null;
  videoUrl = '';
  ingredients: IngredientRow[] = [{ name: '', quantity: null, unit: '' }];
  steps: StepRow[] = [{ instruction: '' }];
  readonly showOriginalIngredients = signal(false);
  readonly showOriginalSteps = signal(false);

  // Tags as signals so the autocomplete suggestions stay reactive
  readonly tags = signal<string[]>([]);
  readonly tagInput = signal('');
  private readonly knownTags = signal<string[]>([]);
  readonly tagOptions = computed(() => {
    const query = this.tagInput().trim().toLowerCase();
    const selected = this.tags().map(t => t.toLowerCase());
    return this.knownTags()
      .filter(name => !selected.includes(name.toLowerCase()))
      .filter(name => !query || name.toLowerCase().includes(query));
  });

  /** Known ingredient names, so existing spellings win over new variants */
  private readonly knownIngredients = signal<string[]>([]);

  // Dietary facts: only submitted when the user touched the picker, so an
  // untouched form keeps AI detection (create) or the stored facts (edit)
  readonly containsClasses = signal<string[]>([]);
  readonly factsStatus = signal<DietaryFactsStatus>(DietaryFactsStatus.Unassessed);
  readonly factsTouched = signal(false);
  readonly factsStatusKey = computed(() => {
    if (this.factsTouched()) {
      return 'recipeForm.factsWillBeConfirmed';
    }
    switch (this.factsStatus()) {
      case DietaryFactsStatus.AiDetected: return 'recipeForm.factsAiDetected';
      case DietaryFactsStatus.UserConfirmed: return 'recipeForm.factsConfirmed';
      default: return 'recipeForm.factsNotAssessed';
    }
  });

  setContainsClasses(classes: string[]): void {
    this.containsClasses.set(classes);
    this.factsTouched.set(true);
  }

  /** When set, the new recipe is linked to this planned meal after saving
   *  (entry point: the shopping list's "still to decide" section) */
  private linkMealId: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private recipesService: RecipesService,
    private plannedMealsService: PlannedMealsService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    void this.detectCameraCaptureAvailability();

    this.recipesService.getRecipeTags().subscribe({
      next: tags => this.knownTags.set(tags.map(t => t.name)),
      error: () => { /* autocomplete is a convenience; typing tags still works */ }
    });

    this.recipesService.getIngredientNames().subscribe({
      next: names => this.knownIngredients.set(names),
      error: () => { /* autocomplete is a convenience; typing ingredients still works */ }
    });

    const queryParams = this.route.snapshot.queryParamMap;
    this.linkMealId = queryParams.get('linkMealId');
    const prefilledTitle = queryParams.get('title');
    if (prefilledTitle) {
      this.title = prefilledTitle;
    }

    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      return;
    }

    this.editingId.set(id);
    this.loading.set(true);
    this.recipesService.getRecipe(id).subscribe({
      next: recipe => {
        this.title = recipe.title;
        this.description = recipe.description ?? '';
        this.originalTitle = recipe.originalTitle;
        this.originalDescription = recipe.originalDescription;
        this.contentLanguage = recipe.contentLanguage;
        this.servings = recipe.servings;
        this.prepTimeMinutes = recipe.prepTimeMinutes ?? null;
        this.cookTimeMinutes = recipe.cookTimeMinutes ?? null;
        this.category = recipe.category ?? '';
        this.keywords = recipe.keywords ?? '';
        this.currentImageUrl.set(recipe.imageUrl ?? null);
        this.imageSourceUrl.set(recipe.imageSourceUrl ?? '');
        this.videoUrl = recipe.videoUrl ?? '';
        this.ingredients = recipe.ingredients.length > 0
          ? recipe.ingredients.map(i => ({
              name: i.name,
              quantity: i.quantity ?? null,
              unit: i.unit ?? '',
              originalText: i.originalText
            }))
          : [{ name: '', quantity: null, unit: '' }];
        this.steps = recipe.steps.length > 0
          ? recipe.steps.map(s => ({ instruction: s.instruction, originalInstruction: s.originalInstruction }))
          : [{ instruction: '' }];
        this.tags.set([...recipe.tags]);
        this.containsClasses.set([...recipe.dietaryFacts.contains]);
        this.factsStatus.set(recipe.dietaryFacts.status);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.snackBar.open('Could not load the recipe', 'Dismiss', { duration: 4000 });
        this.router.navigate(['/recipes']);
      }
    });
  }

  private async detectCameraCaptureAvailability(): Promise<void> {
    if (typeof document === 'undefined' || typeof navigator === 'undefined') {
      return;
    }

    const fileInput = document.createElement('input');
    const mediaDevices = navigator.mediaDevices;
    if (!('capture' in fileInput)
        || typeof mediaDevices?.getUserMedia !== 'function'
        || typeof mediaDevices.enumerateDevices !== 'function') {
      return;
    }

    try {
      const devices = await mediaDevices.enumerateDevices();
      this.cameraCaptureAvailable.set(devices.some(device => device.kind === 'videoinput'));
    } catch {
      // Camera enumeration can be blocked by the browser, permissions policy, or
      // an insecure context. In each case the capture action is not usable here.
      this.cameraCaptureAvailable.set(false);
    }
  }

  ngOnDestroy(): void {
    this.clearSelectedImage();
  }

  selectImage(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) {
      return;
    }
    if (file.type && !file.type.startsWith('image/')) {
      this.snackBar.open('Choose an image file', 'Dismiss', { duration: 4000 });
      return;
    }
    if (file.size > RecipeFormPage.maxImageBytes) {
      this.snackBar.open('Images may be at most 15 MB', 'Dismiss', { duration: 4000 });
      return;
    }

    this.clearSelectedImage();
    this.selectedImageFile = file;
    this.selectedImagePreviewUrl.set(URL.createObjectURL(file));
    this.urlImagePreviewUrl.set(null);
    this.imageSourceUrl.set('');
    this.imageUrlDraft.set('');
    this.imageUrlEditorOpen.set(false);
    this.sourceInfoOpen.set(false);
    this.imageRemoved.set(false);
  }

  openImageUrlEditor(): void {
    this.imageUrlDraft.set(this.imageSourceUrl());
    this.imageUrlEditorOpen.set(true);
  }

  cancelImageUrlEditor(): void {
    this.imageUrlDraft.set(this.imageSourceUrl());
    this.imageUrlEditorOpen.set(false);
  }

  confirmImageUrl(): void {
    const value = this.imageUrlDraft().trim();
    try {
      const parsed = new URL(value);
      if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
        throw new Error('Unsupported protocol');
      }
    } catch {
      this.snackBar.open('Enter a valid http(s) image URL', 'Dismiss', { duration: 4000 });
      return;
    }

    this.clearSelectedImage();
    this.imageSourceUrl.set(value);
    this.imageUrlDraft.set(value);
    this.urlImagePreviewUrl.set(value);
    this.imageRemoved.set(false);
    this.sourceInfoOpen.set(false);
    this.imageUrlEditorOpen.set(false);
  }

  removeImage(): void {
    this.clearSelectedImage();
    this.urlImagePreviewUrl.set(null);
    this.imageSourceUrl.set('');
    this.imageUrlDraft.set('');
    this.imageUrlEditorOpen.set(false);
    this.sourceInfoOpen.set(false);
    this.imageRemoved.set(true);
  }

  showSourceInfo(): void {
    this.sourceInfoOpen.set(true);
  }

  private clearSelectedImage(): void {
    const previewUrl = this.selectedImagePreviewUrl();
    if (previewUrl) {
      URL.revokeObjectURL(previewUrl);
    }
    this.selectedImagePreviewUrl.set(null);
    this.selectedImageFile = null;
  }

  addIngredient(): void {
    this.ingredients.push({ name: '', quantity: null, unit: '' });
  }

  /** Ingredient suggestions matching the typed text (top 8, only while typing) */
  ingredientOptions(typed: string): string[] {
    const query = typed.trim().toLowerCase();
    if (!query) {
      return [];
    }
    return this.knownIngredients()
      .filter(name => name.toLowerCase().includes(query) && name.toLowerCase() !== query)
      .slice(0, 8);
  }

  removeIngredient(index: number): void {
    this.ingredients.splice(index, 1);
  }

  hasOriginalIngredients(): boolean {
    return this.ingredients.some(ingredient => this.originalIngredientText(ingredient) !== null);
  }

  originalIngredientText(ingredient: IngredientRow): string | null {
    const current = [ingredient.quantity, ingredient.unit, ingredient.name]
      .filter(value => value !== undefined && value !== null && String(value).trim())
      .join(' ');
    return this.comparisonOriginal(ingredient.originalText, current);
  }

  addTagFromInput(event: MatChipInputEvent): void {
    this.addTag(event.value);
    event.chipInput.clear();
  }

  selectTag(event: MatAutocompleteSelectedEvent): void {
    this.addTag(event.option.viewValue);
    event.option.deselect();
  }

  removeTag(tag: string): void {
    this.tags.update(tags => tags.filter(t => t !== tag));
  }

  private addTag(value: string): void {
    const name = value.trim().slice(0, 50);
    if (!name) {
      return;
    }
    this.tags.update(tags =>
      tags.some(t => t.toLowerCase() === name.toLowerCase()) ? tags : [...tags, name]);
    this.tagInput.set('');
  }

  addStep(): void {
    this.steps.push({ instruction: '' });
  }

  removeStep(index: number): void {
    this.steps.splice(index, 1);
  }

  hasOriginalSteps(): boolean {
    return this.steps.some(step => this.originalStepInstruction(step) !== null);
  }

  originalStepInstruction(step: StepRow): string | null {
    return this.comparisonOriginal(step.originalInstruction, step.instruction);
  }

  private comparisonOriginal(original: string | undefined, current: string): string | null {
    const originalValue = original?.trim();
    if (!originalValue) {
      return null;
    }
    const normalize = (value: string) => value.replace(/\s+/g, ' ').trim().toLocaleLowerCase();
    return normalize(originalValue) === normalize(current) ? null : originalValue;
  }

  canSave(): boolean {
    return this.title.trim().length > 0 && this.servings >= 1;
  }

  save(): void {
    if (!this.canSave()) {
      return;
    }

    // A typed-but-unconfirmed tag still counts (no lost input on save)
    this.addTag(this.tagInput());

    const payload: CreateRecipe = {
      title: this.title.trim(),
      originalTitle: this.originalTitle,
      description: this.description.trim() || undefined,
      originalDescription: this.originalDescription,
      contentLanguage: this.contentLanguage,
      servings: this.servings,
      prepTimeMinutes: this.prepTimeMinutes ?? undefined,
      cookTimeMinutes: this.cookTimeMinutes ?? undefined,
      totalTimeMinutes: this.prepTimeMinutes || this.cookTimeMinutes
        ? (this.prepTimeMinutes ?? 0) + (this.cookTimeMinutes ?? 0)
        : undefined,
      category: this.category.trim() || undefined,
      keywords: this.keywords.trim() || undefined,
      // A file/camera image is uploaded after the recipe has an id. URL images are
      // downloaded and normalized by the recipe create/update request itself.
      imageUrl: this.selectedImageFile || this.imageRemoved()
        ? undefined
        : this.imageSourceUrl().trim() || undefined,
      videoUrl: this.videoUrl.trim() || undefined,
      ingredients: this.ingredients
        .filter(i => i.name.trim())
        .map(i => ({
          name: i.name.trim(),
          quantity: i.quantity ?? undefined,
          unit: i.unit.trim() || undefined,
          originalText: i.originalText
        })),
      steps: this.steps
        .filter(s => s.instruction.trim())
        .map(s => ({ instruction: s.instruction.trim(), originalInstruction: s.originalInstruction })),
      tags: this.tags(),
      containsClasses: this.factsTouched() ? this.containsClasses() : null
    };

    this.saving.set(true);
    const editingId = this.editingId();
    const request = editingId
      ? this.recipesService.updateRecipe(editingId, payload)
      : this.recipesService.createRecipe(payload);

    let savedRecipe: Recipe | null = null;
    request.pipe(
      tap(recipe => { savedRecipe = recipe; }),
      switchMap(recipe => this.persistPendingImage(recipe))
    ).subscribe({
      next: recipe => {
        this.saving.set(false);
        if (this.linkMealId) {
          this.linkToMealAndReturn(this.linkMealId, recipe.id, recipe.title);
        } else {
          this.router.navigate(['/recipes', recipe.id]);
        }
      },
      error: () => {
        this.saving.set(false);
        this.snackBar.open(
          savedRecipe ? 'Recipe saved, but the image could not be updated' : 'Could not save the recipe',
          'Dismiss',
          { duration: 5000 });
        if (savedRecipe && !this.editingId()) {
          // The create succeeded, so move to its edit URL before a retry; otherwise
          // pressing Save again would create a duplicate recipe.
          this.router.navigate(['/recipes', savedRecipe.id, 'edit'], { replaceUrl: true });
        }
      }
    });
  }

  private persistPendingImage(recipe: Recipe): Observable<Recipe> {
    if (this.selectedImageFile) {
      return this.recipesService.setRecipeImage(recipe.id, this.selectedImageFile)
        .pipe(map(() => recipe));
    }
    if (this.imageRemoved()) {
      return this.recipesService.deleteRecipeImage(recipe.id)
        .pipe(map(() => recipe));
    }
    return of(recipe);
  }

  /** Completes the shopping list flow: attach the new recipe to the meal, go back */
  private linkToMealAndReturn(mealId: string, recipeId: string, title: string): void {
    this.plannedMealsService.setRecipe(mealId, recipeId).subscribe({
      next: () => {
        this.snackBar.open(`"${title}" created and linked to the plan`, 'Dismiss', { duration: 4000 });
        this.router.navigate(['/shopping-list']);
      },
      error: () => {
        // The recipe exists; only the link failed — land on the recipe instead
        this.snackBar.open('Recipe saved, but linking to the meal failed', 'Dismiss', { duration: 5000 });
        this.router.navigate(['/recipes', recipeId]);
      }
    });
  }
}
