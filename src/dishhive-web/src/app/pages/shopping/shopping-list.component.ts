import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { firstValueFrom } from 'rxjs';
import { ShoppingListsService } from '../../services';
import { WeekPlansService } from '../../services/week-plans.service';
import { ShoppingList, ShoppingListItem, WeekPlan } from '../../models';

@Component({
  selector: 'app-shopping-list',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatButtonModule, MatCardModule, MatCheckboxModule,
    MatIconModule, MatSelectModule, MatFormFieldModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <header class="page__header"><h1>Shopping lists</h1></header>

      <mat-card>
        <mat-card-content class="row">
          <mat-form-field appearance="outline" class="grow">
            <mat-label>Generate from week plan</mat-label>
            <mat-select [(ngModel)]="selectedPlanId">
              @for (p of plans(); track p.id) {
                <mat-option [value]="p.id">Week of {{ p.weekStart }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
          <button mat-raised-button color="primary" (click)="generate()" [disabled]="!selectedPlanId">
            <mat-icon>auto_awesome</mat-icon> Generate
          </button>
        </mat-card-content>
      </mat-card>

      @for (list of lists(); track list.id) {
        <mat-card class="list">
          <mat-card-header>
            <mat-card-title>{{ list.title }}</mat-card-title>
            <div class="actions">
              <button mat-stroked-button (click)="exportMd(list)"><mat-icon>download</mat-icon> Markdown</button>
              <button mat-icon-button (click)="remove(list)"><mat-icon>delete</mat-icon></button>
            </div>
          </mat-card-header>
          <mat-card-content>
            @for (sec of grouped(list); track sec.section) {
              <h3>{{ sec.section }}</h3>
              @for (i of sec.items; track i.id) {
                <div class="item">
                  <mat-checkbox [checked]="i.checked" (change)="toggle(list, i, $event.checked)">
                    @if (i.quantity) { <span>{{ i.quantity }} {{ i.unit }}</span> }
                    {{ i.name }}
                  </mat-checkbox>
                </div>
              }
            }
          </mat-card-content>
        </mat-card>
      }
    </div>
  `,
  styles: [`
    .page { padding: 16px; max-width: 900px; margin: 0 auto; }
    .row { display: flex; gap: 8px; align-items: center; }
    .grow { flex: 1; }
    .list { margin-block-end: 16px; }
    .actions { margin-inline-start: auto; }
    .item { padding-block: 4px; }
    h3 { margin-block: 12px 4px; }
  `],
})
export class ShoppingListComponent {
  private readonly svc = inject(ShoppingListsService);
  private readonly plansSvc = inject(WeekPlansService);

  readonly lists = signal<ShoppingList[]>([]);
  readonly plans = signal<WeekPlan[]>([]);
  selectedPlanId = '';

  constructor() {
    this.reload();
    this.plansSvc.list().subscribe((p) => this.plans.set(p));
  }

  private async reload() {
    const items = await firstValueFrom(this.svc.list());
    this.lists.set(items);
  }

  grouped(list: ShoppingList) {
    const map = new Map<string, ShoppingListItem[]>();
    for (const i of list.items) {
      const key = i.section ?? 'Other';
      const arr = map.get(key) ?? [];
      arr.push(i);
      map.set(key, arr);
    }
    return [...map.entries()].map(([section, items]) => ({ section, items }));
  }

  async generate() {
    if (!this.selectedPlanId) return;
    await firstValueFrom(this.svc.generate(this.selectedPlanId));
    await this.reload();
  }

  async toggle(list: ShoppingList, item: ShoppingListItem, checked: boolean) {
    const updated: ShoppingListItem = { ...item, checked };
    await firstValueFrom(this.svc.toggleItem(list.id, item.id, updated));
    item.checked = checked;
  }

  async remove(list: ShoppingList) {
    if (!confirm(`Delete "${list.title}"?`)) return;
    await firstValueFrom(this.svc.deleteList(list.id));
    await this.reload();
  }

  async exportMd(list: ShoppingList) {
    const md = await firstValueFrom(this.svc.exportMarkdown(list.id));
    const blob = new Blob([md], { type: 'text/markdown' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${list.title}.md`;
    a.click();
    URL.revokeObjectURL(url);
  }
}
