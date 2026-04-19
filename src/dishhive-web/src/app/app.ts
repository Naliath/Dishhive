import { Component } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatToolbarModule,
    MatButtonModule,
    MatIconModule,
    MatSidenavModule,
    MatListModule,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  title = 'Dishhive';

  navItems = [
    { path: '/', label: 'Week Planner', icon: 'calendar_today' },
    { path: '/recipes', label: 'Recipes', icon: 'menu_book' },
    { path: '/family', label: 'Family', icon: 'people' },
    { path: '/statistics', label: 'Statistics', icon: 'bar_chart' },
    { path: '/settings', label: 'Settings', icon: 'settings' },
  ];
}
