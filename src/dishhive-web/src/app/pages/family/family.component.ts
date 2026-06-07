import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { firstValueFrom } from 'rxjs';
import { FamilyService } from '../../services/family.service';
import { FamilyMember, PreferenceKind } from '../../models';

const KINDS: PreferenceKind[] = ['Allergy', 'Dislike', 'Like', 'Diet'];

@Component({
  selector: 'app-family',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatButtonModule, MatCardModule, MatChipsModule,
    MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <header class="page__header"><h1>Family members</h1></header>

      <mat-card class="adder">
        <mat-card-content class="row">
          <mat-form-field appearance="outline" class="grow"><mat-label>Name</mat-label>
            <input matInput [(ngModel)]="newName" /></mat-form-field>
          <button mat-raised-button color="primary" (click)="add()" [disabled]="!newName.trim()"><mat-icon>add</mat-icon> Add</button>
        </mat-card-content>
      </mat-card>

      @for (m of members(); track m.id) {
        <mat-card class="member">
          <mat-card-header>
            <mat-card-title>{{ m.displayName }}</mat-card-title>
            <button mat-icon-button (click)="remove(m)"><mat-icon>delete</mat-icon></button>
          </mat-card-header>
          <mat-card-content>
            <mat-chip-set>
              @for (p of m.preferences; track p.id) {
                <mat-chip (removed)="removePref(m, p.id)">
                  {{ p.kind }}: {{ p.value }}
                  <button matChipRemove><mat-icon>cancel</mat-icon></button>
                </mat-chip>
              }
            </mat-chip-set>
            <div class="row">
              <mat-form-field appearance="outline">
                <mat-label>Kind</mat-label>
                <mat-select [(ngModel)]="prefDraft[m.id]!.kind">
                  @for (k of kinds; track k) { <mat-option [value]="k">{{ k }}</mat-option> }
                </mat-select>
              </mat-form-field>
              <mat-form-field appearance="outline" class="grow">
                <mat-label>Value</mat-label>
                <input matInput [(ngModel)]="prefDraft[m.id]!.value" />
              </mat-form-field>
              <button mat-stroked-button (click)="addPref(m)" [disabled]="!prefDraft[m.id]!.value.trim()"><mat-icon>add</mat-icon></button>
            </div>
          </mat-card-content>
        </mat-card>
      }
    </div>
  `,
  styles: [`
    .page { padding: 16px; max-width: 800px; margin: 0 auto; }
    .row { display: flex; gap: 8px; align-items: center; }
    .grow { flex: 1; }
    .adder, .member { margin-block-end: 16px; }
  `],
})
export class FamilyComponent {
  private readonly svc = inject(FamilyService);
  readonly members = signal<FamilyMember[]>([]);
  readonly kinds = KINDS;

  newName = '';
  prefDraft: Record<string, { kind: PreferenceKind; value: string }> = {};

  constructor() { this.reload(); }

  private async reload() {
    const items = await firstValueFrom(this.svc.list());
    this.members.set(items);
    items.forEach((m) => (this.prefDraft[m.id] ??= { kind: 'Allergy', value: '' }));
  }

  async add() {
    await firstValueFrom(this.svc.create({ displayName: this.newName.trim() }));
    this.newName = '';
    await this.reload();
  }

  async remove(m: FamilyMember) {
    if (!confirm(`Remove ${m.displayName}?`)) return;
    await firstValueFrom(this.svc.remove(m.id));
    await this.reload();
  }

  async addPref(m: FamilyMember) {
    const draft = this.prefDraft[m.id]!;
    await firstValueFrom(this.svc.addPreference(m.id, { kind: draft.kind, value: draft.value.trim() }));
    draft.value = '';
    await this.reload();
  }

  async removePref(m: FamilyMember, prefId: string) {
    await firstValueFrom(this.svc.removePreference(m.id, prefId));
    await this.reload();
  }
}
