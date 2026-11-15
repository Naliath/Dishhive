import { ChangeDetectionStrategy, Component, effect, inject, input, OnDestroy, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { Subscription } from 'rxjs';
import { RecipeImportCapabilitiesDto } from '../../api/generated/dishhive-api.client';
import { Recipe } from '../../models/recipe.model';
import { RecipeImportPreference, RecipeImportSource } from '../../services/recipe-import-preference.service';
import { LanguageService, TranslatePipe } from '../../services/language.service';
import { RecipesService } from '../../services/recipes.service';
import { CookingLoaderComponent } from '../cooking-loader/cooking-loader';

@Component({
  selector: 'app-recipe-import', standalone: true,
  imports: [FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule, TranslatePipe, CookingLoaderComponent],
  templateUrl: './recipe-import.html', styleUrl: './recipe-import.scss', changeDetection: ChangeDetectionStrategy.OnPush
})
export class RecipeImportComponent implements OnDestroy {
  readonly visible = input(false);
  readonly completed = output<Recipe>();
  readonly preference = inject(RecipeImportPreference);
  private readonly recipes = inject(RecipesService);
  private readonly language = inject(LanguageService);
  readonly capabilities = signal<RecipeImportCapabilitiesDto | null>(null);
  readonly importing = signal(false);
  readonly checking = signal(false);
  readonly error = signal('');
  readonly photo = signal<File | null>(null);
  readonly preview = signal('');
  readonly cameraAvailable = typeof document !== 'undefined' && 'capture' in document.createElement('input')
    && typeof matchMedia !== 'undefined' && matchMedia('(pointer: coarse)').matches;
  url = '';
  text = '';
  private request?: Subscription;
  private availabilityRequest?: Subscription;
  private visionRequest?: Subscription;

  constructor() {
    effect(() => {
      if (this.visible()) untracked(() => this.loadCapabilities());
    });
  }

  loadCapabilities(): void {
    this.availabilityRequest?.unsubscribe();
    this.availabilityRequest = this.recipes.getImportCapabilities().subscribe({
      next: value => { this.capabilities.set(value); if (this.preference.source() === 'photo' && value.textAvailable && value.photoState === 'notChecked') this.checkPhoto(); },
      error: () => this.error.set(this.language.t('recipeImport.capabilitiesFailed'))
    });
  }

  selectSource(source: RecipeImportSource): void {
    if (this.importing()) return;
    this.preference.select(source);
    this.error.set('');
    if (source === 'photo' && this.capabilities()?.textAvailable && this.capabilities()?.photoState === 'notChecked') this.checkPhoto();
  }

  checkPhoto(): void {
    if (this.checking() || this.importing()) return;
    this.checking.set(true);
    this.visionRequest?.unsubscribe();
    this.visionRequest = this.recipes.checkImportImageCapability().subscribe({
      next: value => { this.capabilities.set(value); this.checking.set(false); },
      error: () => { this.checking.set(false); this.error.set(this.language.t('recipeImport.capabilitiesFailed')); }
    });
  }

  selectPhoto(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file || this.importing()) return;
    if (file.size > (this.capabilities()?.maxImageBytes ?? 15 * 1024 * 1024)) {
      this.error.set(this.language.t('recipeImport.photoTooLarge')); return;
    }
    this.clearPhoto();
    this.photo.set(file);
    this.preview.set(URL.createObjectURL(file));
    this.error.set('');
  }

  clearPhoto(): void {
    if (this.preview()) URL.revokeObjectURL(this.preview());
    this.preview.set(''); this.photo.set(null);
  }

  canImport(): boolean {
    if (this.importing()) return false;
    if (this.preference.source() === 'url') return !!this.url.trim();
    if (!this.capabilities()?.textAvailable) return false;
    if (this.preference.source() === 'text') return !!this.text.trim() && this.text.length <= (this.capabilities()?.maxTextCharacters ?? 30000);
    return !!this.photo() && this.capabilities()?.photoState === 'available' && !this.checking();
  }

  import(): void {
    if (!this.canImport()) return;
    this.importing.set(true); this.error.set('');
    const source = this.preference.source();
    const request = source === 'url' ? this.recipes.importRecipe(this.url.trim())
      : source === 'text' ? this.recipes.importRecipeText(this.text) : this.recipes.importRecipeImage(this.photo()!);
    this.request = request.subscribe({
      next: recipe => {
        this.importing.set(false);
        if (source === 'url') this.url = '';
        else if (source === 'text') this.text = '';
        else this.clearPhoto();
        this.completed.emit(recipe);
      },
      error: error => {
        this.importing.set(false);
        this.error.set([error.error?.title, error.error?.detail].filter(Boolean).join(' ') || this.language.t('recipeImport.failed'));
      }
    });
  }

  cancel(): void {
    this.request?.unsubscribe(); this.importing.set(false);
    this.error.set(this.language.t('recipeImport.cancelled'));
  }

  ngOnDestroy(): void { this.request?.unsubscribe(); this.availabilityRequest?.unsubscribe(); this.visionRequest?.unsubscribe(); this.clearPhoto(); }
}
