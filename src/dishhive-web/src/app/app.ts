import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { Router, RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { SettingsService } from './services/settings.service';
import { OnboardingService } from './services/onboarding.service';
import { PwaService } from './services/pwa.service';
import { ThemeService } from './services/theme.service';
import { OnboardingComponent } from './components/onboarding/onboarding';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatIconRegistry } from '@angular/material/icon';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatDividerModule } from '@angular/material/divider';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { LanguageService, TranslatePipe } from './services/language.service';

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
    MatDividerModule,
    MatProgressSpinnerModule,
    OnboardingComponent,
    TranslatePipe
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class App implements OnInit {
  title = 'Dishhive';
  readonly checkingOnboarding = signal(true);
  readonly showOnboarding = signal(false);

  constructor(
    private settingsService: SettingsService,
    private languageService: LanguageService,
    private onboardingService: OnboardingService,
    private router: Router,
    iconRegistry: MatIconRegistry,
    // Instantiated for its side effects: update checks, offline notices, install prompt
    private pwaService: PwaService,
    // Instantiated for its side effects: restores saved theme preference on startup
    private themeService: ThemeService
  ) {
    iconRegistry.setDefaultFontSetClass('material-symbols-outlined');
  }

  ngOnInit(): void {
    // Load display preferences once so all pages use them from the start
    this.settingsService.loadPreferences().subscribe(preferences => {
      if (preferences) this.languageService.use(preferences.preferredLanguage);
    });
    this.onboardingService.start().subscribe({
      next: status => {
        this.showOnboarding.set(status.shouldShow);
        this.checkingOnboarding.set(false);
      },
      // Onboarding must never lock users out when the startup check is unavailable.
      error: () => this.checkingOnboarding.set(false)
    });
  }

  finishOnboarding(): void {
    this.showOnboarding.set(false);
    void this.router.navigateByUrl('/');
  }
}
