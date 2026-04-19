import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogRef, MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { RecipeSummary } from '../../models/recipe.model';
import { DayOfWeek, PlannedMeal } from '../../models/week-plan.model';

export interface PlanMealDialogData {
  day: DayOfWeek;
  existingMeal?: PlannedMeal;
  recipes: RecipeSummary[];
}

export interface PlanMealDialogResult {
  recipeId?: string;
  vagueInstruction?: string;
  notes?: string;
  isFromFreezer: boolean;
}

@Component({
  selector: 'app-plan-meal-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatInputModule,
    MatFormFieldModule,
    MatSelectModule,
    MatAutocompleteModule,
    MatIconModule,
    MatCheckboxModule,
  ],
  template: `
    <h2 mat-dialog-title>Plan meal for {{ data.day }}</h2>

    <mat-dialog-content>
      <div class="dialog-form">

        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Recipe</mat-label>
          <mat-select [(ngModel)]="selectedRecipeId" (ngModelChange)="onRecipeSelected($event)">
            <mat-option [value]="null">— no specific recipe —</mat-option>
            <mat-option *ngFor="let recipe of data.recipes" [value]="recipe.id">
              {{ recipe.title }}
            </mat-option>
          </mat-select>
        </mat-form-field>

        <mat-form-field appearance="outline" class="full-width" *ngIf="!selectedRecipeId">
          <mat-label>Or describe loosely…</mat-label>
          <input matInput [(ngModel)]="vagueInstruction"
                 placeholder="e.g. something with pasta, leftovers, fish" />
        </mat-form-field>

        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Notes (optional)</mat-label>
          <textarea matInput [(ngModel)]="notes" rows="2"
                    placeholder="e.g. double portion, no onion for kids"></textarea>
        </mat-form-field>

        <mat-checkbox [(ngModel)]="isFromFreezer">
          <mat-icon style="vertical-align:middle;margin-right:4px">kitchen</mat-icon>
          Using something from the freezer
        </mat-checkbox>

      </div>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button (click)="onCancel()">Cancel</button>
      <button mat-button color="warn" *ngIf="data.existingMeal" (click)="onClear()">Clear</button>
      <button mat-flat-button color="primary" (click)="onSave()"
              [disabled]="!selectedRecipeId && !vagueInstruction.trim()">
        Save
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .dialog-form { display: flex; flex-direction: column; gap: 8px; min-width: 340px; padding-top: 8px; }
    .full-width { width: 100%; }
  `]
})
export class PlanMealDialogComponent {
  selectedRecipeId: string | null;
  vagueInstruction: string;
  notes: string;
  isFromFreezer: boolean;

  constructor(
    public dialogRef: MatDialogRef<PlanMealDialogComponent, PlanMealDialogResult | 'clear' | null>,
    @Inject(MAT_DIALOG_DATA) public data: PlanMealDialogData
  ) {
    const m = data.existingMeal;
    this.selectedRecipeId = m?.recipeId ?? null;
    this.vagueInstruction = m?.vagueInstruction ?? '';
    this.notes = m?.notes ?? '';
    this.isFromFreezer = m?.isFromFreezer ?? false;
  }

  onRecipeSelected(id: string | null): void {
    if (id) this.vagueInstruction = '';
  }

  onSave(): void {
    this.dialogRef.close({
      recipeId: this.selectedRecipeId ?? undefined,
      vagueInstruction: this.selectedRecipeId ? undefined : this.vagueInstruction?.trim() || undefined,
      notes: this.notes?.trim() || undefined,
      isFromFreezer: this.isFromFreezer,
    });
  }

  onClear(): void {
    this.dialogRef.close('clear');
  }

  onCancel(): void {
    this.dialogRef.close(null);
  }
}
