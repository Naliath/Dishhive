import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';

interface StatisticsOverview {
  recipeCount: number;
  familyMemberCount: number;
  weekPlanCount: number;
  plannedMealCount: number;
}

interface TopRecipe {
  recipeId: string;
  title: string;
  pictureUrl?: string;
  timesPlanned: number;
}

interface WeekSummary {
  weekStartDate: string;
  totalMeals: number;
  mealsWithRecipe: number;
  mealsFromFreezer: number;
}

@Component({
  selector: 'app-statistics',
  standalone: true,
  imports: [
    CommonModule,
    MatProgressSpinnerModule,
    MatCardModule,
    MatIconModule,
  ],
  template: `
    <div class="page-container medium">
      <h1 class="section-title">Statistics</h1>

      @if (loading()) {
        <div class="loading-container"><mat-spinner diameter="48" /></div>
      } @else {
        @if (overview()) {
          <div class="stats-grid">
            <mat-card class="stat-card">
              <mat-card-content>
                <mat-icon class="stat-icon">menu_book</mat-icon>
                <div class="stat-value">{{ overview()!.recipeCount }}</div>
                <div class="stat-label">Recipes</div>
              </mat-card-content>
            </mat-card>
            <mat-card class="stat-card">
              <mat-card-content>
                <mat-icon class="stat-icon">group</mat-icon>
                <div class="stat-value">{{ overview()!.familyMemberCount }}</div>
                <div class="stat-label">Family members</div>
              </mat-card-content>
            </mat-card>
            <mat-card class="stat-card">
              <mat-card-content>
                <mat-icon class="stat-icon">date_range</mat-icon>
                <div class="stat-value">{{ overview()!.weekPlanCount }}</div>
                <div class="stat-label">Weeks planned</div>
              </mat-card-content>
            </mat-card>
            <mat-card class="stat-card">
              <mat-card-content>
                <mat-icon class="stat-icon">restaurant</mat-icon>
                <div class="stat-value">{{ overview()!.plannedMealCount }}</div>
                <div class="stat-label">Meals planned</div>
              </mat-card-content>
            </mat-card>
          </div>
        }

        @if (topRecipes().length > 0) {
          <h2>Most planned recipes (last 12 weeks)</h2>
          <div class="top-recipes">
            @for (recipe of topRecipes(); track recipe.recipeId; let i = $index) {
              <div class="top-recipe-row">
                <span class="rank">{{ i + 1 }}</span>
                <span class="recipe-name">{{ recipe.title }}</span>
                <span class="times-planned">{{ recipe.timesPlanned }}×</span>
              </div>
            }
          </div>
        }

        @if (recentWeeks().length > 0) {
          <h2>Recent weeks</h2>
          <div class="recent-weeks">
            @for (week of recentWeeks(); track week.weekStartDate) {
              <div class="week-row">
                <span class="week-date">{{ week.weekStartDate | date:'d MMM' }}</span>
                <span class="week-meals">{{ week.totalMeals }} meals</span>
                @if (week.mealsFromFreezer > 0) {
                  <span class="week-freezer">
                    <mat-icon style="font-size:16px;height:16px;width:16px;">kitchen</mat-icon>
                    {{ week.mealsFromFreezer }} from freezer
                  </span>
                }
              </div>
            }
          </div>
        }
      }
    </div>
  `,
  styles: [`
    .stats-grid {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 16px;
      margin-bottom: 32px;
      @media (max-width: 700px) { grid-template-columns: repeat(2, 1fr); }
    }
    .stat-card mat-card-content {
      display: flex; flex-direction: column; align-items: center; padding: 24px 16px;
    }
    .stat-icon { font-size: 36px; height: 36px; width: 36px; color: var(--mat-sys-primary); margin-bottom: 8px; }
    .stat-value { font-size: 2rem; font-weight: 700; color: var(--mat-sys-on-surface); }
    .stat-label { font-size: 0.85rem; color: var(--mat-sys-on-surface-variant); }
    .top-recipes { display: flex; flex-direction: column; gap: 8px; margin-bottom: 32px; }
    .top-recipe-row { display: flex; align-items: center; gap: 16px; padding: 8px 12px;
      background: var(--mat-sys-surface-variant); border-radius: 8px; }
    .rank { font-weight: 700; color: var(--mat-sys-primary); width: 24px; }
    .recipe-name { flex: 1; }
    .times-planned { font-weight: 600; color: var(--mat-sys-secondary); }
    .recent-weeks { display: flex; flex-direction: column; gap: 6px; }
    .week-row { display: flex; align-items: center; gap: 16px; padding: 6px 12px;
      border-bottom: 1px solid var(--mat-sys-outline-variant); }
    .week-date { width: 70px; font-weight: 500; }
    .week-meals { color: var(--mat-sys-on-surface-variant); }
    .week-freezer { display: flex; align-items: center; gap: 4px; font-size: 0.85rem; color: var(--mat-sys-primary); }
    .loading-container { display: flex; justify-content: center; margin-top: 80px; }
  `]
})
export class StatisticsPage implements OnInit {
  overview = signal<StatisticsOverview | null>(null);
  topRecipes = signal<TopRecipe[]>([]);
  recentWeeks = signal<WeekSummary[]>([]);
  loading = signal(true);

  constructor(private http: HttpClient) {}

  ngOnInit(): void {
    let pending = 3;
    const done = () => { if (--pending === 0) this.loading.set(false); };

    this.http.get<StatisticsOverview>('/api/statistics/overview').subscribe({
      next: (v) => { this.overview.set(v); done(); },
      error: () => done()
    });
    this.http.get<TopRecipe[]>('/api/statistics/top-recipes').subscribe({
      next: (v) => { this.topRecipes.set(v); done(); },
      error: () => done()
    });
    this.http.get<WeekSummary[]>('/api/statistics/recent-weeks').subscribe({
      next: (v) => { this.recentWeeks.set(v); done(); },
      error: () => done()
    });
  }
}
