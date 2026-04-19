import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatListModule } from '@angular/material/list';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDividerModule } from '@angular/material/divider';
import { WeekPlannerService, ShoppingListItem } from '../../services/week-planner.service';

@Component({
  selector: 'app-shopping-list',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatListModule,
    MatCheckboxModule,
    MatDividerModule,
  ],
  template: `
    <div class="page-container narrow">
      <div class="back-row">
        <button mat-button routerLink="/">
          <mat-icon>arrow_back</mat-icon> Back to planner
        </button>
      </div>

      <h1 class="section-title">Shopping list</h1>
      @if (weekStartDate()) {
        <p class="week-label">Week of {{ weekStartDate() }}</p>
      }

      @if (loading()) {
        <div class="loading-container"><mat-spinner diameter="48" /></div>
      } @else if (items().length === 0) {
        <div class="empty-state">
          <mat-icon>shopping_cart</mat-icon>
          <h3>Nothing to buy</h3>
          <p>Plan some meals with recipes for this week first.</p>
        </div>
      } @else {
        <div class="list-actions">
          <button mat-stroked-button (click)="printList()">
            <mat-icon>print</mat-icon> Print
          </button>
          <button mat-stroked-button (click)="shareList()">
            <mat-icon>share</mat-icon> Share
          </button>
        </div>

        <mat-list>
          @for (item of items(); track item.name; let last = $last) {
            <mat-list-item class="list-item" [class.checked]="checkedItems.has(item.name)">
              <mat-checkbox
                [checked]="checkedItems.has(item.name)"
                (change)="toggleItem(item.name)"
                matListItemIcon>
              </mat-checkbox>
              <span matListItemTitle [class.strikethrough]="checkedItems.has(item.name)">
                {{ item.name }}
              </span>
              <span matListItemLine class="amounts">{{ item.amounts.join(', ') }}</span>
              <span matListItemMeta class="from-recipes">{{ item.recipeNames.join(', ') }}</span>
            </mat-list-item>
            @if (!last) { <mat-divider /> }
          }
        </mat-list>
      }
    </div>
  `,
  styles: [`
    .back-row { margin-bottom: 16px; }
    .week-label { color: var(--mat-sys-on-surface-variant); margin-bottom: 16px; }
    .list-actions { display: flex; gap: 12px; margin-bottom: 16px; }
    .list-item { border-radius: 8px; }
    .list-item.checked { opacity: 0.5; }
    .strikethrough { text-decoration: line-through; }
    .amounts { font-weight: 500; color: var(--mat-sys-primary); }
    .from-recipes { font-size: 0.75rem; color: var(--mat-sys-on-surface-variant); }
    .loading-container { display: flex; justify-content: center; margin-top: 80px; }
  `]
})
export class ShoppingListPage implements OnInit {
  items = signal<ShoppingListItem[]>([]);
  loading = signal(true);
  weekStartDate = signal<string | null>(null);
  checkedItems = new Set<string>();

  constructor(
    private weekPlannerService: WeekPlannerService,
    private route: ActivatedRoute
  ) {}

  ngOnInit(): void {
    const week = this.route.snapshot.queryParamMap.get('week');
    if (!week) { this.loading.set(false); return; }
    this.weekStartDate.set(week);
    this.weekPlannerService.getShoppingList(week).subscribe({
      next: (items) => { this.items.set(items); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  toggleItem(name: string): void {
    if (this.checkedItems.has(name)) {
      this.checkedItems.delete(name);
    } else {
      this.checkedItems.add(name);
    }
    // trigger change detection for Set
    this.checkedItems = new Set(this.checkedItems);
  }

  printList(): void {
    window.print();
  }

  shareList(): void {
    const text = this.items()
      .map(i => `${i.name}${i.amounts.length ? ' — ' + i.amounts.join(', ') : ''}`)
      .join('\n');
    if (navigator.share) {
      navigator.share({ title: 'Shopping list', text });
    } else {
      navigator.clipboard?.writeText(text);
    }
  }
}
