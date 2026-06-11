import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { IntegrationsService } from '../../services/integrations.service';
import { IntegrationStatusResponse } from '../../models/integration-status.model';

@Component({
  selector: 'app-integrations-status',
  standalone: true,
  imports: [MatCardModule, MatIconModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './integrations-status.html',
  styleUrl: './integrations-status.scss'
})
export class IntegrationsStatusComponent implements OnInit {
  readonly integrations = signal<IntegrationStatusResponse | null>(null);

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
}
