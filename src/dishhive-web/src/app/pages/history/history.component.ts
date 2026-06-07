import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import { HistoryService } from '../../services';
import { DishFrequency, DishHistoryEntry } from '../../models';

@Component({
  selector: 'app-history',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatTableModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <h1>History &amp; statistics</h1>
      <mat-card>
        <mat-card-header><mat-card-title>Most-planned dishes</mat-card-title></mat-card-header>
        <mat-card-content>
          <table mat-table [dataSource]="frequency()">
            <ng-container matColumnDef="dish">
              <th mat-header-cell *matHeaderCellDef>Dish</th>
              <td mat-cell *matCellDef="let r">{{ r.dishLabel }}</td>
            </ng-container>
            <ng-container matColumnDef="times">
              <th mat-header-cell *matHeaderCellDef>Times</th>
              <td mat-cell *matCellDef="let r">{{ r.timesPlanned }}</td>
            </ng-container>
            <ng-container matColumnDef="last">
              <th mat-header-cell *matHeaderCellDef>Last</th>
              <td mat-cell *matCellDef="let r">{{ r.lastPlanned }}</td>
            </ng-container>
            <tr mat-header-row *matHeaderRowDef="cols"></tr>
            <tr mat-row *matRowDef="let row; columns: cols"></tr>
          </table>
        </mat-card-content>
      </mat-card>

      <mat-card class="recent">
        <mat-card-header><mat-card-title>Recent meals</mat-card-title></mat-card-header>
        <mat-card-content>
          @for (h of history(); track h.id) {
            <div class="row">
              <span class="date">{{ h.date }}</span>
              <span class="meal">{{ h.mealType }}</span>
              <span class="dish">{{ h.dishLabel }}</span>
            </div>
          }
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .page { padding: 16px; max-width: 900px; margin: 0 auto; }
    .recent { margin-block-start: 16px; }
    .row { display: grid; grid-template-columns: 110px 90px 1fr; gap: 8px; padding-block: 4px; }
    .date, .meal { color: var(--mat-sys-on-surface-variant); }
    table { width: 100%; }
  `],
})
export class HistoryComponent {
  private readonly svc = inject(HistoryService);

  readonly cols = ['dish', 'times', 'last'];
  readonly history = signal<DishHistoryEntry[]>([]);
  readonly frequency = signal<DishFrequency[]>([]);

  constructor() {
    this.svc.list().subscribe((h) => this.history.set(h));
    this.svc.frequency(20).subscribe((f) => this.frequency.set(f));
  }
}
