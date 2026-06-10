import { Component } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [MatCardModule, MatIconModule],
  template: `
    <mat-card style="max-width: 600px; margin: 2rem auto; text-align: center;">
      <div mat-card-avatar style="background: #4caf50; width: 80px; height: 80px; border-radius: 50%; display: flex; align-items: center; justify-content: center; margin: 0 auto;">
        <mat-icon style="font-size: 40px; width: 40px; height: 40px; color: white;">restaurant</mat-icon>
      </div>
      <mat-card-title style="margin-top: 1rem;">Welcome to Dishhive</mat-card-title>
      <mat-card-subtitle>Family Week Menu Planning</mat-card-subtitle>
      <mat-card-content style="margin-top: 1rem;">
        <p>Plan your family week menu, manage recipes, and generate shopping lists.</p>
        <p><em>Application scaffold ready. Features coming soon.</em></p>
      </mat-card-content>
    </mat-card>
  `,
  styles: [`
    :host {
      display: block;
      padding: 2rem;
    }
  `]
})
export class HomeComponent {}
