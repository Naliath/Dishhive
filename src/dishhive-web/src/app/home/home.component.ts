import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';

@Component({
  selector: 'dh-home',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatIconModule, MatButtonModule],
  template: `
    <div class="home-container">
      <mat-card class="welcome-card">
        <mat-card-header>
          <mat-card-title>Welcome to Dishhive</mat-card-title>
          <mat-card-subtitle>Your family meal planning companion</mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <p>Plan weekly menus, manage recipes, and generate shopping lists for your household.</p>
        </mat-card-content>
      </mat-card>

      <div class="feature-grid">
        <mat-card class="feature-card" *ngFor="let feature of features">
          <mat-card-content>
            <mat-icon class="feature-icon">{{ feature.icon }}</mat-icon>
            <h3>{{ feature.title }}</h3>
            <p>{{ feature.description }}</p>
          </mat-card-content>
        </mat-card>
      </div>
    </div>
  `,
  styles: [`
    .home-container {
      max-width: 960px;
      margin: 0 auto;
    }

    .welcome-card {
      margin-bottom: 32px;
    }

    .feature-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
      gap: 16px;
    }

    .feature-card {
      text-align: center;
    }

    .feature-icon {
      font-size: 48px;
      width: 48px;
      height: 48px;
      margin-bottom: 8px;
      color: #f57f17;
    }
  `],
})
export class HomeComponent {
  features = [
    { icon: 'people', title: 'Family', description: 'Manage household members, preferences, and dietary needs.' },
    { icon: 'restaurant_menu', title: 'Recipes', description: 'Store and organize your family recipes.' },
    { icon: 'calendar_month', title: 'Week Planner', description: 'Plan meals for the entire week.' },
    { icon: 'shopping_cart', title: 'Shopping List', description: 'Generate shopping lists from your planned menu.' },
    { icon: 'assessment', title: 'History', description: 'Review past meals and track favorites.' },
    { icon: 'settings', title: 'Settings', description: 'Configure measurement preferences and more.' },
  ];
}
