import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterOutlet, RouterModule } from '@angular/router';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';

@Component({
  selector: 'dh-root',
  standalone: true,
  imports: [
    CommonModule,
    RouterOutlet,
    RouterModule,
    MatSidenavModule,
    MatToolbarModule,
    MatListModule,
    MatIconModule,
    MatButtonModule,
  ],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.scss'],
})
export class AppComponent {
  title = 'Dishhive';
  isSidebarOpened = true;

  navItems = [
    { label: 'Home', icon: 'home', route: '/' },
    { label: 'Family', icon: 'people', route: '/family' },
    { label: 'Recipes', icon: 'restaurant', route: '/recipes' },
    { label: 'Week Planner', icon: 'calendar_month', route: '/planner' },
    { label: 'Shopping List', icon: 'shopping_cart', route: '/shopping-list' },
    { label: 'Settings', icon: 'settings', route: '/settings' },
  ];

  toggleSidebar(): void {
    this.isSidebarOpened = !this.isSidebarOpened;
  }
}
