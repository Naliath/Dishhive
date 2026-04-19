import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDividerModule } from '@angular/material/divider';
import { FamilyService } from '../../services/family.service';
import { FamilyMember, MemberPreference, FavoriteDish, PreferenceType } from '../../models/family-member.model';

const PREFERENCE_COLORS: Record<PreferenceType, string> = {
  Allergy: 'warn',
  Intolerance: 'accent',
  DietaryConstraint: 'primary',
  Dislike: '',
  Preference: 'primary',
};

const PREFERENCE_ICONS: Record<PreferenceType, string> = {
  Allergy: 'warning',
  Intolerance: 'report_problem',
  DietaryConstraint: 'block',
  Dislike: 'thumb_down',
  Preference: 'favorite',
};

@Component({
  selector: 'app-family-member-detail',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatButtonModule,
    MatIconModule,
    MatCardModule,
    MatChipsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    MatDividerModule,
  ],
  template: `
    <div class="page-container medium">
      @if (loading()) {
        <div class="loading-container"><mat-spinner diameter="48" /></div>
      } @else if (!member()) {
        <div class="empty-state">
          <mat-icon>person_off</mat-icon>
          <h3>Member not found</h3>
          <button mat-flat-button (click)="back()">Back to family</button>
        </div>
      } @else {
        <!-- Header -->
        <div class="page-header">
          <div class="member-header">
            <button mat-icon-button (click)="back()" aria-label="Back">
              <mat-icon>arrow_back</mat-icon>
            </button>
            <div class="member-avatar-lg">
              <mat-icon>{{ member()!.isGuest ? 'person_outline' : 'person' }}</mat-icon>
            </div>
            <div>
              <h1 class="section-title" style="margin:0;">{{ member()!.name }}</h1>
              <p class="subtitle">
                @if (member()!.isGuest) {
                  Guest{{ member()!.guestUntil ? ' · until ' + (member()!.guestUntil | date:'d MMM y') : '' }}
                } @else {
                  Household member
                }
              </p>
            </div>
          </div>
          <button mat-stroked-button color="warn" (click)="deleteMember()">
            <mat-icon>delete_outline</mat-icon> Remove
          </button>
        </div>

        <mat-divider style="margin-bottom: 24px;" />

        <!-- Dietary Preferences -->
        <section class="section">
          <h2 class="section-subtitle">Dietary preferences</h2>

          <div class="chips-container">
            @for (pref of member()!.preferences; track pref.id) {
              <mat-chip class="pref-chip" [ngClass]="'pref-' + pref.preferenceType.toLowerCase()">
                <mat-icon matChipAvatar>{{ prefIcon(pref.preferenceType) }}</mat-icon>
                {{ pref.value }}
                @if (pref.notes) { <span class="chip-notes"> · {{ pref.notes }}</span> }
                <button matChipRemove (click)="removePreference(pref)">
                  <mat-icon>cancel</mat-icon>
                </button>
              </mat-chip>
            }
            @if (member()!.preferences.length === 0) {
              <p class="empty-hint">No dietary preferences recorded.</p>
            }
          </div>

          @if (showPrefForm()) {
            <div class="inline-form">
              <mat-form-field appearance="outline" style="flex: 0 0 160px;">
                <mat-label>Type</mat-label>
                <mat-select [(ngModel)]="newPrefType">
                  @for (t of preferenceTypes; track t) {
                    <mat-option [value]="t">{{ t }}</mat-option>
                  }
                </mat-select>
              </mat-form-field>
              <mat-form-field appearance="outline" style="flex: 1;">
                <mat-label>Value (e.g. Peanuts, Gluten-free)</mat-label>
                <input matInput [(ngModel)]="newPrefValue" (keydown.enter)="addPreference()" />
              </mat-form-field>
              <mat-form-field appearance="outline" style="flex: 1;">
                <mat-label>Notes (optional)</mat-label>
                <input matInput [(ngModel)]="newPrefNotes" />
              </mat-form-field>
              <button mat-flat-button color="primary" (click)="addPreference()" [disabled]="!newPrefValue.trim()">Add</button>
              <button mat-button (click)="showPrefForm.set(false)">Cancel</button>
            </div>
          } @else {
            <button mat-stroked-button (click)="showPrefForm.set(true)">
              <mat-icon>add</mat-icon> Add preference
            </button>
          }
        </section>

        <mat-divider style="margin: 24px 0;" />

        <!-- Favourite Dishes -->
        <section class="section">
          <h2 class="section-subtitle">Favourite dishes</h2>

          <div class="favorites-list">
            @for (fav of member()!.favoriteDishes; track fav.id) {
              <div class="favorite-item">
                <mat-icon class="fav-icon">favorite</mat-icon>
                <span class="fav-name">{{ fav.dishName }}</span>
                <button mat-icon-button color="warn" (click)="removeFavorite(fav)">
                  <mat-icon>remove_circle_outline</mat-icon>
                </button>
              </div>
            }
            @if (member()!.favoriteDishes.length === 0) {
              <p class="empty-hint">No favourite dishes yet.</p>
            }
          </div>

          @if (showFavForm()) {
            <div class="inline-form">
              <mat-form-field appearance="outline" style="flex: 1;">
                <mat-label>Dish name</mat-label>
                <input matInput [(ngModel)]="newFavName" (keydown.enter)="addFavorite()" />
              </mat-form-field>
              <button mat-flat-button color="primary" (click)="addFavorite()" [disabled]="!newFavName.trim()">Add</button>
              <button mat-button (click)="showFavForm.set(false)">Cancel</button>
            </div>
          } @else {
            <button mat-stroked-button (click)="showFavForm.set(true)">
              <mat-icon>add</mat-icon> Add favourite
            </button>
          }
        </section>
      }
    </div>
  `,
  styles: [`
    .page-header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 16px; }
    .member-header { display: flex; align-items: center; gap: 16px; }
    .member-avatar-lg {
      width: 56px; height: 56px; border-radius: 50%;
      background: var(--mat-sys-primary-container);
      display: flex; align-items: center; justify-content: center;
      mat-icon { font-size: 32px; width: 32px; height: 32px; color: var(--mat-sys-on-primary-container); }
    }
    .subtitle { color: var(--mat-sys-on-surface-variant); margin: 4px 0 0; }
    .section-subtitle { font-size: 1rem; font-weight: 600; margin: 0 0 16px; }
    .section { margin-bottom: 8px; }
    .chips-container { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 16px; min-height: 32px; }
    .empty-hint { color: var(--mat-sys-on-surface-variant); font-size: 0.875rem; margin: 0; }
    .pref-chip { cursor: default; }
    .pref-allergy { background-color: var(--mat-sys-error-container) !important; color: var(--mat-sys-on-error-container) !important; }
    .pref-intolerance { background-color: #fff3cd !important; }
    .pref-dietaryconstraint { background-color: var(--mat-sys-primary-container) !important; color: var(--mat-sys-on-primary-container) !important; }
    .chip-notes { opacity: 0.75; font-size: 0.8em; }
    .inline-form { display: flex; flex-wrap: wrap; gap: 12px; align-items: flex-end; margin-bottom: 12px; }
    .favorites-list { display: flex; flex-direction: column; gap: 4px; margin-bottom: 16px; }
    .favorite-item { display: flex; align-items: center; gap: 8px; padding: 6px 0; }
    .fav-icon { color: var(--mat-sys-error); font-size: 18px; width: 18px; height: 18px; }
    .fav-name { flex: 1; }
    .loading-container { display: flex; justify-content: center; margin-top: 80px; }
  `]
})
export class FamilyMemberDetailPage implements OnInit {
  member = signal<FamilyMember | null>(null);
  loading = signal(true);

  showPrefForm = signal(false);
  newPrefType: PreferenceType = 'Preference';
  newPrefValue = '';
  newPrefNotes = '';

  showFavForm = signal(false);
  newFavName = '';

  readonly preferenceTypes: PreferenceType[] = ['Allergy', 'Intolerance', 'DietaryConstraint', 'Dislike', 'Preference'];

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private familyService: FamilyService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id')!;
    this.familyService.getById(id).subscribe({
      next: (m) => { this.member.set(m); this.loading.set(false); },
      error: () => { this.member.set(null); this.loading.set(false); }
    });
  }

  back(): void { this.router.navigate(['/family']); }

  prefIcon(type: PreferenceType): string { return PREFERENCE_ICONS[type] ?? 'label'; }

  addPreference(): void {
    if (!this.newPrefValue.trim()) return;
    const id = this.member()!.id;
    this.familyService.addPreference(id, this.newPrefType, this.newPrefValue.trim(), this.newPrefNotes.trim() || undefined)
      .subscribe({
        next: (pref) => {
          this.member.update(m => m ? ({ ...m, preferences: [...m.preferences, pref] }) : m);
          this.newPrefValue = '';
          this.newPrefNotes = '';
          this.showPrefForm.set(false);
          this.snackBar.open('Preference added', undefined, { duration: 2000 });
        },
        error: () => this.snackBar.open('Failed to add preference', 'Dismiss', { duration: 3000 })
      });
  }

  removePreference(pref: MemberPreference): void {
    const id = this.member()!.id;
    this.familyService.deletePreference(id, pref.id).subscribe({
      next: () => {
        this.member.update(m => m ? ({ ...m, preferences: m.preferences.filter(p => p.id !== pref.id) }) : m);
        this.snackBar.open('Preference removed', undefined, { duration: 2000 });
      },
      error: () => this.snackBar.open('Failed to remove preference', 'Dismiss', { duration: 3000 })
    });
  }

  addFavorite(): void {
    if (!this.newFavName.trim()) return;
    const id = this.member()!.id;
    this.familyService.addFavorite(id, this.newFavName.trim()).subscribe({
      next: (fav) => {
        this.member.update(m => m ? ({ ...m, favoriteDishes: [...m.favoriteDishes, fav] }) : m);
        this.newFavName = '';
        this.showFavForm.set(false);
        this.snackBar.open('Favourite added', undefined, { duration: 2000 });
      },
      error: () => this.snackBar.open('Failed to add favourite', 'Dismiss', { duration: 3000 })
    });
  }

  removeFavorite(fav: FavoriteDish): void {
    const id = this.member()!.id;
    this.familyService.deleteFavorite(id, fav.id).subscribe({
      next: () => {
        this.member.update(m => m ? ({ ...m, favoriteDishes: m.favoriteDishes.filter(f => f.id !== fav.id) }) : m);
        this.snackBar.open('Favourite removed', undefined, { duration: 2000 });
      },
      error: () => this.snackBar.open('Failed to remove favourite', 'Dismiss', { duration: 3000 })
    });
  }

  deleteMember(): void {
    if (!confirm(`Remove ${this.member()!.name} from the household?`)) return;
    this.familyService.delete(this.member()!.id).subscribe({
      next: () => {
        this.snackBar.open(`${this.member()!.name} removed`, undefined, { duration: 2000 });
        this.router.navigate(['/family']);
      },
      error: () => this.snackBar.open('Failed to remove member', 'Dismiss', { duration: 3000 })
    });
  }
}
