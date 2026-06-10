import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { ShoppingListService } from '../../services/shopping-list.service';
import { MeasurementService } from '../../services/measurement.service';
import { ShoppingList } from '../../models/shopping-list.model';

function toIso(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

function mondayOf(date: Date): Date {
  const monday = new Date(date);
  monday.setHours(0, 0, 0, 0);
  monday.setDate(monday.getDate() - ((monday.getDay() + 6) % 7));
  return monday;
}

@Component({
  selector: 'app-shopping-list-page',
  standalone: true,
  imports: [
    DatePipe,
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatListModule,
    MatProgressSpinnerModule,
    MatSnackBarModule
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shopping-list.page.html',
  styleUrl: './shopping-list.page.scss'
})
export class ShoppingListPage implements OnInit {
  readonly list = signal<ShoppingList | null>(null);
  readonly loading = signal(true);

  private from = '';
  private to = '';

  constructor(
    private route: ActivatedRoute,
    private shoppingListService: ShoppingListService,
    public measurementService: MeasurementService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    // Range from query params (planner entry point) or default to the current week
    const params = this.route.snapshot.queryParamMap;
    const monday = mondayOf(new Date());
    const sunday = new Date(monday);
    sunday.setDate(sunday.getDate() + 6);

    this.from = params.get('from') ?? toIso(monday);
    this.to = params.get('to') ?? toIso(sunday);
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.shoppingListService.getShoppingList(this.from, this.to).subscribe({
      next: list => {
        this.list.set(list);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.snackBar.open('Could not generate the shopping list', 'Dismiss', { duration: 4000 });
      }
    });
  }

  async copyAsText(): Promise<void> {
    const list = this.list();
    if (!list) {
      return;
    }

    const lines: string[] = [`Shopping list ${list.from} – ${list.to}`, ''];
    for (const item of list.items) {
      const quantity = this.measurementService.format(item.quantity, item.unit);
      lines.push(quantity ? `- ${quantity} ${item.name}` : `- ${item.name}`);
    }
    if (list.reminders.length > 0) {
      lines.push('', 'Still to decide:');
      for (const reminder of list.reminders) {
        lines.push(`- ${reminder.date}: ${reminder.text}`);
      }
    }

    try {
      await navigator.clipboard.writeText(lines.join('\n'));
      this.snackBar.open('Shopping list copied to clipboard', 'Dismiss', { duration: 3000 });
    } catch {
      this.snackBar.open('Could not access the clipboard', 'Dismiss', { duration: 4000 });
    }
  }
}
