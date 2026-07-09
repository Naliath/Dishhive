import { Component, OnInit } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { SettingsService } from './services/settings.service';
import { PwaService } from './services/pwa.service';
import { ThemeService } from './services/theme.service';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatDividerModule } from '@angular/material/divider';

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
    MatDividerModule
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App implements OnInit {
  title = 'Dishhive';

  constructor(
    private settingsService: SettingsService,
    // Instantiated for its side effects: update checks, offline notices, install prompt
    private pwaService: PwaService,
    // Instantiated for its side effects: restores saved theme preference on startup
    private themeService: ThemeService
  ) {}

  ngOnInit(): void {
    // Load display preferences once so all pages use them from the start
    this.settingsService.loadMeasurementSystem().subscribe();
    this.settingsService.loadFirstDayOfWeek().subscribe();
  }
}
