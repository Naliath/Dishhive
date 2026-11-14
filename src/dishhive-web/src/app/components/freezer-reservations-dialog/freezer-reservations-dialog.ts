import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { HttpErrorResponse } from '@angular/common/http';
import { FreezerReconciliationDto, FreezerReservationIssueDto, FreezerReservationMealDto } from '../../api/generated/dishhive-api.client';
import { FreezerService } from '../../services/freezer.service';
import { LanguageService, LocalizedDatePipe, TranslatePipe } from '../../services/language.service';

export interface FreezerReservationsResult { changed: boolean; editMealId?: string; }

@Component({
  selector: 'app-freezer-reservations-dialog',
  imports: [MatButtonModule, MatCardModule, MatDialogModule, LocalizedDatePipe, TranslatePipe],
  templateUrl: './freezer-reservations-dialog.html',
  styleUrl: './freezer-reservations-dialog.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FreezerReservationsDialog implements OnInit {
  private readonly service = inject(FreezerService);
  private readonly language = inject(LanguageService);
  private readonly dialog = inject<MatDialogRef<FreezerReservationsDialog, FreezerReservationsResult>>(MatDialogRef);
  private changed = false;
  readonly data = signal<FreezerReconciliationDto | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');

  ngOnInit(): void { this.refresh(); }
  refresh(): void {
    this.busy.set(true);
    this.error.set('');
    this.service.getReservations().subscribe({
      next: data => { this.data.set(data); this.busy.set(false); },
      error: () => { this.error.set(this.language.t('freezerReview.loadFailed')); this.busy.set(false); }
    });
  }
  resolve(issue: FreezerReservationIssueDto, meal: FreezerReservationMealDto, action: 'release' | 'confirm'): void {
    this.busy.set(true);
    this.error.set('');
    this.service.resolveReservation(meal.id, {
      itemRef: issue.itemRef, quantity: meal.quantity, action, fingerprint: issue.fingerprint
    }).subscribe({
      next: () => { this.changed = true; this.refresh(); },
      error: (error: HttpErrorResponse) => {
        this.error.set(error.error?.detail ?? this.language.t('freezerReview.saveFailed'));
        this.busy.set(false);
      }
    });
  }
  edit(id: string): void { this.dialog.close({ changed: this.changed, editMealId: id }); }
  close(): void { this.dialog.close({ changed: this.changed }); }
}
