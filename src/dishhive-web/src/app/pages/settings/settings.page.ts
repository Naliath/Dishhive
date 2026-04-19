import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatDividerModule } from '@angular/material/divider';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { FormsModule } from '@angular/forms';
import { SettingsService, WeekStartDay, MeasurementSystem } from '../../services/settings.service';

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [
    CommonModule,
    MatIconModule,
    MatListModule,
    MatDividerModule,
    MatSelectModule,
    MatSnackBarModule,
    FormsModule,
  ],
  template: `
    <div class="page-container narrow">
      <h1 class="section-title">Settings</h1>

      <mat-list>
        <mat-list-item>
          <mat-icon matListItemIcon>today</mat-icon>
          <span matListItemTitle>Week starts on</span>
          <span matListItemLine class="setting-hint">Used in the week planner calendar</span>
          <div matListItemMeta>
            <mat-select
              [(ngModel)]="selectedWeekStartDay"
              (ngModelChange)="onWeekStartDayChange($event)"
              style="width: 130px;">
              <mat-option *ngFor="let day of weekDayOptions" [value]="day">{{ day }}</mat-option>
            </mat-select>
          </div>
        </mat-list-item>

        <mat-divider />

        <mat-list-item>
          <mat-icon matListItemIcon>straighten</mat-icon>
          <span matListItemTitle>Measurement system</span>
          <span matListItemLine class="setting-hint">Used for recipe ingredient quantities</span>
          <div matListItemMeta>
            <mat-select
              [(ngModel)]="selectedMeasurement"
              (ngModelChange)="onMeasurementChange($event)"
              style="width: 130px;">
              <mat-option value="metric">Metric</mat-option>
              <mat-option value="imperial">Imperial</mat-option>
            </mat-select>
          </div>
        </mat-list-item>

        <mat-divider />

        <mat-list-item>
          <mat-icon matListItemIcon>info</mat-icon>
          <span matListItemTitle>Version</span>
          <span matListItemLine class="version-text">{{ version }}</span>
        </mat-list-item>
      </mat-list>
    </div>
  `,
  styles: [`
    .setting-hint { color: var(--mat-sys-on-surface-variant); font-size: 12px; }
    .version-text { color: var(--mat-sys-on-surface-variant); }
  `]
})
export class SettingsPage implements OnInit {
  readonly weekDayOptions: WeekStartDay[] = [
    'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'
  ];

  selectedWeekStartDay: WeekStartDay = 'Monday';
  selectedMeasurement: MeasurementSystem = 'metric';
  version = '0.1.0';

  constructor(private settings: SettingsService, private snackBar: MatSnackBar) {}

  ngOnInit(): void {
    this.settings.loadAll().subscribe({
      next: () => {
        this.selectedWeekStartDay = this.settings.weekStartDay();
        this.selectedMeasurement = this.settings.measurementSystem();
      },
      error: () => {
        this.selectedWeekStartDay = this.settings.weekStartDay();
        this.selectedMeasurement = this.settings.measurementSystem();
      }
    });
  }

  onWeekStartDayChange(day: WeekStartDay): void {
    this.settings.setWeekStartDay(day).subscribe({
      next: () => this.snackBar.open(`Week now starts on ${day}`, undefined, { duration: 2000 }),
      error: () => this.snackBar.open('Failed to save setting', 'Dismiss', { duration: 3000 })
    });
  }

  onMeasurementChange(system: MeasurementSystem): void {
    this.settings.setMeasurementSystem(system).subscribe({
      next: () => this.snackBar.open(`Switched to ${system} measurements`, undefined, { duration: 2000 }),
      error: () => this.snackBar.open('Failed to save setting', 'Dismiss', { duration: 3000 })
    });
  }
}
