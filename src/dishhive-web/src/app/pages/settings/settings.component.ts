import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { firstValueFrom } from 'rxjs';
import { SettingsService } from '../../services/settings.service';
import { AgentsService, AgentStatus, LearnedRecipeSource } from '../../services';
import { AppSettings, MeasurementSystem } from '../../models';

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    MatButtonModule, MatButtonToggleModule, MatCardModule, MatIconModule, MatSlideToggleModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <h1>Settings</h1>
      @if (settings(); as s) {
        <mat-card>
          <mat-card-content>
            <h3>Measurement system</h3>
            <mat-button-toggle-group [value]="s.measurementSystem" (change)="setSystem($event.value)">
              <mat-button-toggle value="Metric">Metric (g, ml)</mat-button-toggle>
              <mat-button-toggle value="Imperial">Imperial (oz, fl oz)</mat-button-toggle>
            </mat-button-toggle-group>

            <h3>Freezy integration</h3>
            <mat-slide-toggle [checked]="s.freezyEnabled" (change)="setFreezy($event.checked)">
              Show frozen items as a meal source
            </mat-slide-toggle>
          </mat-card-content>
        </mat-card>
      }

      <mat-card>
        <mat-card-content>
          <h3>AI assistant</h3>
          @if (aiStatus(); as a) {
            @if (a.available) {
              <p class="ok">Active \u2014 model {{ a.model }} via {{ a.provider }}.</p>
            } @else {
              <p class="muted">
                Disabled. Configure <code>Dishhive:Ai:Provider</code> and an API key on the server to enable
                meal-planning suggestions and adaptive recipe import.
              </p>
            }
          }

          <h3>Learned recipe sources</h3>
          <p class="muted">
            Sites the AI agent has been trained on. Once a blueprint is learned, future imports from that site
            run deterministically without calling the AI.
          </p>
          @if (learned().length === 0) {
            <p class="muted"><em>No learned sources yet.</em></p>
          } @else {
            <ul class="learned">
              @for (l of learned(); track l.host) {
                <li>
                  <span><strong>{{ l.host }}</strong> \u00b7 {{ l.strategy }} \u00b7 used {{ l.useCount }} times</span>
                  <button mat-icon-button (click)="forget(l.host)" aria-label="Forget this source">
                    <mat-icon>delete</mat-icon>
                  </button>
                </li>
              }
            </ul>
          }
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .page { padding: 16px; max-width: 600px; margin: 0 auto; }
    h3 { margin-block-start: 16px; }
    .ok { color: var(--mat-sys-primary); }
    .muted { color: var(--mat-sys-on-surface-variant); }
    ul.learned { list-style: none; padding: 0; margin: 0; }
    ul.learned li { display: flex; align-items: center; justify-content: space-between; padding: 6px 0; border-block-end: 1px solid var(--mat-sys-outline-variant); }
  `],
})
export class SettingsComponent {
  private readonly svc = inject(SettingsService);
  private readonly agents = inject(AgentsService);

  readonly settings = signal<AppSettings | null>(null);
  readonly aiStatus = signal<AgentStatus | null>(null);
  readonly learned = signal<LearnedRecipeSource[]>([]);

  constructor() {
    this.svc.get().subscribe((s) => this.settings.set(s));
    this.agents.status().subscribe((a) => this.aiStatus.set(a));
    this.refreshLearned();
  }

  async setSystem(system: MeasurementSystem) {
    const s = await firstValueFrom(this.svc.setMeasurementSystem(system));
    this.settings.set(s);
  }

  async setFreezy(enabled: boolean) {
    const s = await firstValueFrom(this.svc.setFreezyEnabled(enabled));
    this.settings.set(s);
  }

  async forget(host: string) {
    await firstValueFrom(this.agents.deleteLearnedSource(host));
    this.refreshLearned();
  }

  private refreshLearned() {
    this.agents.learnedSources().subscribe((rows) => this.learned.set(rows));
  }
}
