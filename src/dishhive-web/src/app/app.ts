import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
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
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  readonly nav = [
    { path: '/planner', icon: 'event_note', label: 'Week planner' },
    { path: '/family', icon: 'people', label: 'Family' },
    { path: '/recipes', icon: 'menu_book', label: 'Recipes' },
    { path: '/recipes/import', icon: 'cloud_download', label: 'Import recipe' },
    { path: '/shopping-list', icon: 'shopping_cart', label: 'Shopping list' },
    { path: '/history', icon: 'history', label: 'History' },
    { path: '/settings', icon: 'settings', label: 'Settings' },
  ];
}
