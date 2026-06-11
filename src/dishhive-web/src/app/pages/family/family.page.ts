import { ChangeDetectionStrategy, Component, OnInit, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { COMMA, ENTER } from '@angular/cdk/keycodes';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipInputEvent, MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { forkJoin } from 'rxjs';
import { FamilyMembersService } from '../../services/family-members.service';
import { RecipesService } from '../../services/recipes.service';
import { DietaryTagKind, FamilyMember, FamilyMemberFavorite } from '../../models/family-member.model';
import { RecipeListItem } from '../../models/recipe.model';

type TagField = 'allergy' | 'diet';

@Component({
  selector: 'app-family-page',
  standalone: true,
  imports: [
    FormsModule,
    MatAutocompleteModule,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatListModule,
    MatProgressSpinnerModule,
    MatSlideToggleModule,
    MatSnackBarModule,
    MatTooltipModule
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './family.page.html',
  styleUrl: './family.page.scss'
})
export class FamilyPage implements OnInit {
  readonly separatorKeys = [ENTER, COMMA] as const;

  readonly members = signal<FamilyMember[]>([]);
  readonly favoritesByMember = signal<Map<string, FamilyMemberFavorite[]>>(new Map());
  readonly loading = signal(true);
  readonly formVisible = signal(false);
  readonly editingId = signal<string | null>(null);
  readonly saving = signal(false);

  // Form state; tags are signals so the autocomplete suggestions stay reactive
  name = '';
  isGuest = false;
  preferenceNotes = '';
  readonly allergyTags = signal<string[]>([]);
  readonly dietTags = signal<string[]>([]);
  readonly allergyInput = signal('');
  readonly dietInput = signal('');
  newFavorite = '';
  readonly favoriteRecipeResults = signal<RecipeListItem[]>([]);

  /** Known tag names per kind, from the shared tag pool (for autocomplete) */
  private readonly knownAllergyTags = signal<string[]>([]);
  private readonly knownDietTags = signal<string[]>([]);

  readonly allergyOptions = computed(() =>
    this.filterOptions(this.knownAllergyTags(), this.allergyTags(), this.allergyInput()));
  readonly dietOptions = computed(() =>
    this.filterOptions(this.knownDietTags(), this.dietTags(), this.dietInput()));

  constructor(
    private familyMembersService: FamilyMembersService,
    private recipesService: RecipesService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.loadMembers();
    this.loadKnownTags();
  }

  loadMembers(): void {
    this.loading.set(true);
    this.familyMembersService.getMembers().subscribe({
      next: members => {
        this.members.set(members);
        this.loading.set(false);
        this.loadFavorites(members);
      },
      error: () => {
        this.loading.set(false);
        this.snackBar.open('Could not load family members', 'Dismiss', { duration: 4000 });
      }
    });
  }

  private loadKnownTags(): void {
    this.familyMembersService.getDietaryTags().subscribe({
      next: tags => {
        this.knownAllergyTags.set(tags.filter(t => t.kind === DietaryTagKind.Allergy).map(t => t.name));
        this.knownDietTags.set(tags.filter(t => t.kind === DietaryTagKind.Diet).map(t => t.name));
      },
      error: () => { /* autocomplete is a convenience; typing tags still works */ }
    });
  }

  private loadFavorites(members: FamilyMember[]): void {
    if (members.length === 0) {
      this.favoritesByMember.set(new Map());
      return;
    }
    forkJoin(members.map(m => this.familyMembersService.getFavorites(m.id))).subscribe({
      next: favoriteLists => {
        const map = new Map<string, FamilyMemberFavorite[]>();
        members.forEach((member, index) => map.set(member.id, favoriteLists[index]));
        this.favoritesByMember.set(map);
      },
      error: () => { /* favorites are a non-critical decoration of the member cards */ }
    });
  }

  favoritesOf(memberId: string): FamilyMemberFavorite[] {
    return this.favoritesByMember().get(memberId) ?? [];
  }

  /** Search the recipe store while typing a favorite (same pattern as meal planning) */
  searchFavoriteRecipes(): void {
    const term = this.newFavorite.trim();
    if (!term) {
      this.favoriteRecipeResults.set([]);
      return;
    }
    this.recipesService.getRecipes(term).subscribe({
      next: recipes => this.favoriteRecipeResults.set(recipes.slice(0, 5)),
      error: () => this.favoriteRecipeResults.set([])
    });
  }

  /** Add a favorite linked to a recipe from the search results */
  addFavoriteRecipe(recipe: RecipeListItem): void {
    this.saveFavorite({ recipeId: recipe.id, dishName: recipe.title });
  }

  /** Add the typed text as a free-text favorite */
  addFavorite(): void {
    const dishName = this.newFavorite.trim();
    if (dishName) {
      this.saveFavorite({ dishName });
    }
  }

  private saveFavorite(favorite: { recipeId?: string; dishName: string }): void {
    const memberId = this.editingId();
    if (!memberId) {
      return;
    }
    this.familyMembersService.addFavorite(memberId, favorite).subscribe({
      next: () => {
        this.newFavorite = '';
        this.favoriteRecipeResults.set([]);
        this.loadFavorites(this.members());
      },
      error: () => this.snackBar.open('Could not add the favorite', 'Dismiss', { duration: 4000 })
    });
  }

  removeFavorite(favorite: FamilyMemberFavorite): void {
    this.familyMembersService.deleteFavorite(favorite.familyMemberId, favorite.id).subscribe({
      next: () => this.loadFavorites(this.members()),
      error: () => this.snackBar.open('Could not remove the favorite', 'Dismiss', { duration: 4000 })
    });
  }

  startAdd(): void {
    this.editingId.set(null);
    this.resetForm();
    this.formVisible.set(true);
  }

  startEdit(member: FamilyMember): void {
    this.editingId.set(member.id);
    this.name = member.name;
    this.isGuest = member.isGuest;
    this.preferenceNotes = member.preferenceNotes ?? '';
    this.allergyTags.set([...member.allergyTags]);
    this.dietTags.set([...member.dietTags]);
    this.allergyInput.set('');
    this.dietInput.set('');
    this.formVisible.set(true);
  }

  cancelForm(): void {
    this.formVisible.set(false);
    this.editingId.set(null);
  }

  addTagFromInput(field: TagField, event: MatChipInputEvent): void {
    this.addTag(field, event.value);
    event.chipInput.clear();
  }

  selectTag(field: TagField, event: MatAutocompleteSelectedEvent): void {
    this.addTag(field, event.option.viewValue);
    event.option.deselect();
  }

  removeTag(field: TagField, tag: string): void {
    this.tagsOf(field).update(tags => tags.filter(t => t !== tag));
  }

  private addTag(field: TagField, value: string): void {
    const name = value.trim().slice(0, 50);
    if (!name) {
      return;
    }
    this.tagsOf(field).update(tags =>
      tags.some(t => t.toLowerCase() === name.toLowerCase()) ? tags : [...tags, name]);
    this.inputOf(field).set('');
  }

  private tagsOf(field: TagField) {
    return field === 'allergy' ? this.allergyTags : this.dietTags;
  }

  private inputOf(field: TagField) {
    return field === 'allergy' ? this.allergyInput : this.dietInput;
  }

  private filterOptions(known: string[], selected: string[], input: string): string[] {
    const query = input.trim().toLowerCase();
    return known
      .filter(name => !selected.some(s => s.toLowerCase() === name.toLowerCase()))
      .filter(name => !query || name.toLowerCase().includes(query));
  }

  save(): void {
    const name = this.name.trim();
    if (!name) {
      return;
    }

    // A typed-but-unconfirmed tag still counts (no lost input on save)
    this.addTag('allergy', this.allergyInput());
    this.addTag('diet', this.dietInput());

    this.saving.set(true);
    const payload = {
      name,
      isGuest: this.isGuest,
      allergyTags: this.allergyTags(),
      dietTags: this.dietTags(),
      preferenceNotes: this.preferenceNotes.trim() || undefined
    };

    const editingId = this.editingId();
    const request = editingId
      ? this.familyMembersService.updateMember(editingId, { ...payload, isActive: true })
      : this.familyMembersService.createMember(payload);

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.formVisible.set(false);
        this.editingId.set(null);
        this.loadMembers();
        this.loadKnownTags();
      },
      error: () => {
        this.saving.set(false);
        this.snackBar.open('Could not save the member', 'Dismiss', { duration: 4000 });
      }
    });
  }

  remove(member: FamilyMember): void {
    this.familyMembersService.deleteMember(member.id).subscribe({
      next: () => {
        this.snackBar.open(`${member.name} removed`, 'Dismiss', { duration: 3000 });
        this.loadMembers();
        this.loadKnownTags();
      },
      error: () => this.snackBar.open('Could not remove the member', 'Dismiss', { duration: 4000 })
    });
  }

  private resetForm(): void {
    this.name = '';
    this.isGuest = false;
    this.preferenceNotes = '';
    this.allergyTags.set([]);
    this.dietTags.set([]);
    this.allergyInput.set('');
    this.dietInput.set('');
  }
}
