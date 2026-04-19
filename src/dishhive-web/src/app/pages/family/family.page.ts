import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatChipsModule } from '@angular/material/chips';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { FamilyService } from '../../services/family.service';
import { FamilyMemberSummary } from '../../models/family-member.model';
import { AddMemberDialogComponent } from '../../components/add-member-dialog/add-member-dialog.component';

@Component({
  selector: 'app-family',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatButtonModule,
    MatIconModule,
    MatCardModule,
    MatDialogModule,
    MatChipsModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
  ],
  template: `
    <div class="page-container medium">
      <div class="page-header">
        <h1 class="section-title" style="margin:0;">Family</h1>
        <button mat-flat-button color="primary" (click)="openAddMember()">
          <mat-icon>person_add</mat-icon> Add member
        </button>
      </div>

      @if (loading()) {
        <div class="loading-container"><mat-spinner diameter="48" /></div>
      } @else if (members().length === 0) {
        <div class="empty-state">
          <mat-icon>group</mat-icon>
          <h3>No family members yet</h3>
          <p>Add who lives in your household to personalise meal planning.</p>
          <button mat-flat-button color="primary" (click)="openAddMember()">Add first member</button>
        </div>
      } @else {
        <div class="members-grid">
          @for (member of members(); track member.id) {
            <mat-card class="member-card">
              <mat-card-header>
                <div mat-card-avatar class="member-avatar">
                  <mat-icon>{{ member.isGuest ? 'person_outline' : 'person' }}</mat-icon>
                </div>
                <mat-card-title>{{ member.name }}</mat-card-title>
                <mat-card-subtitle>
                  @if (member.isGuest) {
                    Guest
                    @if (member.guestUntil) { · until {{ member.guestUntil | date:'d MMM' }} }
                  } @else {
                    Household member
                  }
                </mat-card-subtitle>
              </mat-card-header>
              <mat-card-actions align="end">
                <button mat-button (click)="viewMember(member)">View</button>
                <button mat-icon-button color="warn" (click)="deleteMember(member)"
                        aria-label="Remove member">
                  <mat-icon>delete_outline</mat-icon>
                </button>
              </mat-card-actions>
            </mat-card>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 20px; }
    .members-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 16px; }
    .member-avatar { background: var(--mat-sys-primary-container); border-radius: 50%;
      display: flex; align-items: center; justify-content: center;
      mat-icon { color: var(--mat-sys-on-primary-container); } }
    .loading-container { display: flex; justify-content: center; margin-top: 80px; }
  `]
})
export class FamilyPage implements OnInit {
  members = signal<FamilyMemberSummary[]>([]);
  loading = signal(true);

  constructor(
    private familyService: FamilyService,
    private router: Router,
    private dialog: MatDialog,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading.set(true);
    this.familyService.getAll().subscribe({
      next: (m) => { this.members.set(m); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  openAddMember(): void {
    const ref = this.dialog.open(AddMemberDialogComponent, { width: '360px' });
    ref.afterClosed().subscribe(result => {
      if (result) { this.load(); this.snackBar.open(`${result.name} added`, undefined, { duration: 2000 }); }
    });
  }

  viewMember(member: FamilyMemberSummary): void {
    this.router.navigate(['/family', member.id]);
  }

  deleteMember(member: FamilyMemberSummary): void {
    if (!confirm(`Remove ${member.name} from the household?`)) return;
    this.familyService.delete(member.id).subscribe({
      next: () => {
        this.members.update(list => list.filter(m => m.id !== member.id));
        this.snackBar.open(`${member.name} removed`, undefined, { duration: 2000 });
      },
      error: () => this.snackBar.open('Failed to remove member', 'Dismiss', { duration: 3000 })
    });
  }
}

