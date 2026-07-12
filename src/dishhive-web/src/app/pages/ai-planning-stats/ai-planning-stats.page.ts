import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { AiPlanningMetricsResponse } from '../../models/integration-status.model';
import { IntegrationsService } from '../../services/integrations.service';

@Component({
  selector: 'app-ai-planning-stats-page',
  standalone: true,
  imports: [DatePipe, DecimalPipe, RouterLink, MatButtonModule, MatCardModule, MatIconModule],
  templateUrl: './ai-planning-stats.page.html',
  styleUrl: './ai-planning-stats.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AiPlanningStatsPage implements OnInit {
  readonly metrics = signal<AiPlanningMetricsResponse | null>(null);
  readonly failed = signal(false);

  constructor(private integrationsService: IntegrationsService) {}

  ngOnInit(): void {
    this.refresh();
  }

  refresh(): void {
    this.failed.set(false);
    this.integrationsService.getAiPlanningMetrics().subscribe({
      next: result => this.metrics.set(result),
      error: () => this.failed.set(true)
    });
  }

  seconds(milliseconds: number): string {
    return `${(milliseconds / 1000).toFixed(1)}s`;
  }
}
