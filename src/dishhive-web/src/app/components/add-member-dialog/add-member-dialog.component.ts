import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogRef, MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { FamilyService } from '../../services/family.service';
import { FamilyMember } from '../../models/family-member.model';

@Component({
  selector: 'app-add-member-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatInputModule,
    MatFormFieldModule,
    MatCheckboxModule,
  ],
  template: `
    <h2 mat-dialog-title>Add family member</h2>
    <mat-dialog-content>
      <div class="form" style="display:flex;flex-direction:column;gap:12px;padding-top:8px;min-width:280px;">
        <mat-form-field appearance="outline">
          <mat-label>Name</mat-label>
          <input matInput [(ngModel)]="name" (keydown.enter)="onSave()" />
        </mat-form-field>
        <mat-checkbox [(ngModel)]="isGuest">Guest (temporary household member)</mat-checkbox>
        @if (isGuest) {
          <mat-form-field appearance="outline">
            <mat-label>Guest until</mat-label>
            <input matInput [(ngModel)]="guestUntil" type="date" />
          </mat-form-field>
        }
        @if (error) { <p style="color:var(--mat-sys-error)">{{ error }}</p> }
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close(null)">Cancel</button>
      <button mat-flat-button color="primary" (click)="onSave()" [disabled]="!name.trim() || saving">
        Add
      </button>
    </mat-dialog-actions>
  `
})
export class AddMemberDialogComponent {
  name = '';
  isGuest = false;
  guestUntil = '';
  saving = false;
  error: string | null = null;

  constructor(
    public dialogRef: MatDialogRef<AddMemberDialogComponent, FamilyMember | null>,
    @Inject(MAT_DIALOG_DATA) _data: void,
    private familyService: FamilyService
  ) {}

  onSave(): void {
    if (!this.name.trim()) return;
    this.saving = true;
    this.familyService.create({
      name: this.name.trim(),
      isGuest: this.isGuest,
      guestUntil: this.isGuest && this.guestUntil ? this.guestUntil : undefined
    }).subscribe({
      next: (m) => this.dialogRef.close(m),
      error: () => { this.error = 'Failed to add member.'; this.saving = false; }
    });
  }
}
