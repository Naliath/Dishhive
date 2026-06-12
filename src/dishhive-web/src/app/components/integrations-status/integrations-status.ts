import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { IntegrationsService } from '../../services/integrations.service';
import { IntegrationStatusResponse, ScraperVersionCheck } from '../../models/integration-status.model';

@Component({
  selector: 'app-integrations-status',
  standalone: true,
  imports: [MatButtonModule, MatCardModule, MatIconModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './integrations-status.html',
  styleUrl: './integrations-status.scss'
})
export class IntegrationsStatusComponent implements OnInit {
  private static readonly updatePollIntervalMs = 2000;
  private static readonly updatePollAttempts = 15;

  readonly integrations = signal<IntegrationStatusResponse | null>(null);
  readonly versionCheck = signal<ScraperVersionCheck | null>(null);
  readonly checkingVersion = signal(false);
  readonly updating = signal(false);
  readonly scraperMessage = signal<string | null>(null);

  constructor(private integrationsService: IntegrationsService) {}

  ngOnInit(): void {
    this.integrationsService.getStatus().subscribe(status => this.integrations.set(status));
  }

  chipClass(integration: { configured: boolean; reachable: boolean }): string {
    if (!integration.configured) return 'status-chip--off';
    return integration.reachable ? 'status-chip--ok' : 'status-chip--warn';
  }

  chipIcon(integration: { configured: boolean; reachable: boolean }): string {
    if (!integration.configured) return 'radio_button_unchecked';
    return integration.reachable ? 'check_circle' : 'warning';
  }

  chipLabel(integration: { configured: boolean; reachable: boolean }): string {
    if (!integration.configured) return 'Not configured';
    return integration.reachable ? 'Active' : 'Unreachable';
  }

  checkForUpdates(): void {
    this.checkingVersion.set(true);
    this.scraperMessage.set(null);
    this.integrationsService.checkScraperVersion().subscribe(result => {
      this.checkingVersion.set(false);
      this.versionCheck.set(result);
      if (!result) {
        this.scraperMessage.set('Could not check for updates — is the scraper container running?');
      }
    });
  }

  installUpdate(): void {
    const target = this.versionCheck()?.latestVersion ?? undefined;
    this.updating.set(true);
    this.scraperMessage.set(null);
    this.integrationsService.updateScraper(target).subscribe(result => {
      if (!result) {
        this.updating.set(false);
        this.scraperMessage.set('Update failed — check the scraper container logs.');
        return;
      }
      // The sidecar restarts to load the new version; poll until it is back
      this.pollUntilScraperBack(result.targetVersion, IntegrationsStatusComponent.updatePollAttempts);
    });
  }

  private pollUntilScraperBack(targetVersion: string | null, attemptsLeft: number): void {
    setTimeout(() => {
      this.integrationsService.getStatus().subscribe(status => {
        const scraper = status?.scraper;
        const back = !!scraper?.reachable
          && (!targetVersion || scraper.packageVersion === targetVersion);

        if (back) {
          this.integrations.set(status);
          this.updating.set(false);
          this.versionCheck.set(null);
          this.scraperMessage.set(`Updated to v${scraper!.packageVersion}.`);
        } else if (attemptsLeft <= 1) {
          if (status) this.integrations.set(status);
          this.updating.set(false);
          this.scraperMessage.set('The scraper service has not come back up yet — refresh the page in a moment.');
        } else {
          this.pollUntilScraperBack(targetVersion, attemptsLeft - 1);
        }
      });
    }, IntegrationsStatusComponent.updatePollIntervalMs);
  }
}
