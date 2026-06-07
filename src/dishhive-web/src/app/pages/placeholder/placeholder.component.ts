import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';

/**
 * Generic placeholder used by every page until its feature is implemented.
 * Reads `title` and `feature` from route data so each section is self-describing.
 */
@Component({
  selector: 'app-placeholder',
  standalone: true,
  imports: [MatCardModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <mat-card>
      <mat-card-header>
        <mat-card-title>{{ title() }}</mat-card-title>
        <mat-card-subtitle>Feature plan: docs/features/{{ feature() }}.md</mat-card-subtitle>
      </mat-card-header>
      <mat-card-content>
        <p>
          This page is a placeholder. The infrastructure is in place; the user-facing
          implementation lands incrementally per the feature plan.
        </p>
      </mat-card-content>
    </mat-card>
  `,
  styles: [
    `
      mat-card {
        margin-block-start: 24px;
      }
    `,
  ],
})
export class PlaceholderComponent {
  private readonly route = inject(ActivatedRoute);

  readonly title = toSignal(
    this.route.data.pipe(map((d) => (d['title'] as string) ?? 'Dishhive')),
    { initialValue: 'Dishhive' },
  );
  readonly feature = toSignal(
    this.route.data.pipe(map((d) => (d['feature'] as string) ?? '')),
    { initialValue: '' },
  );
}
