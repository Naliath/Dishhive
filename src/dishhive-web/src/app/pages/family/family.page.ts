import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { forkJoin } from 'rxjs';
import { FamilyMembersService } from '../../services/family-members.service';
import { FamilyMember, FamilyMemberFavorite } from '../../models/family-member.model';

interface MemberForm {
  name: string;
  isGuest: boolean;
  allergies: string;
  dietaryConstraints: string;
  preferenceNotes: string;
}

const emptyForm = (): MemberForm => ({
  name: '',
  isGuest: false,
  allergies: '',
  dietaryConstraints: '',
  preferenceNotes: ''
});

@Component({
  selector: 'app-family-page',
  standalone: true,
  imports: [
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSlideToggleModule,
    MatSnackBarModule
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './family.page.html',
  styleUrl: './family.page.scss'
})
export class FamilyPage implements OnInit {
  readonly members = signal<FamilyMember[]>([]);
  readonly favoritesByMember = signal<Map<string, FamilyMemberFavorite[]>>(new Map());
  readonly loading = signal(true);
  readonly formVisible = signal(false);
  readonly editingId = signal<string | null>(null);
  readonly saving = signal(false);

  form: MemberForm = emptyForm();
  newFavorite = '';

  constructor(
    private familyMembersService: FamilyMembersService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.loadMembers();
  }

  loadMembers(): void {
    this.loading.set(true);
    this.familyMembersService.getMembers().subscribe({
      next: members => {
        this.members.set(members);
        this.loading.set(false);
        this.loadFavorites(members);
      },
      error: () => {
        this.loading.set(false);
        this.snackBar.open('Could not load family members', 'Dismiss', { duration: 4000 });
      }
    });
  }

  private loadFavorites(members: FamilyMember[]): void {
    if (members.length === 0) {
      this.favoritesByMember.set(new Map());
      return;
    }
    forkJoin(members.map(m => this.familyMembersService.getFavorites(m.id))).subscribe({
      next: favoriteLists => {
        const map = new Map<string, FamilyMemberFavorite[]>();
        members.forEach((member, index) => map.set(member.id, favoriteLists[index]));
        this.favoritesByMember.set(map);
      },
      error: () => { /* favorites are a non-critical decoration of the member cards */ }
    });
  }

  favoritesOf(memberId: string): FamilyMemberFavorite[] {
    return this.favoritesByMember().get(memberId) ?? [];
  }

  addFavorite(): void {
    const memberId = this.editingId();
    const dishName = this.newFavorite.trim();
    if (!memberId || !dishName) {
      return;
    }
    this.familyMembersService.addFavorite(memberId, { dishName }).subscribe({
      next: () => {
        this.newFavorite = '';
        this.loadFavorites(this.members());
      },
      error: () => this.snackBar.open('Could not add the favorite', 'Dismiss', { duration: 4000 })
    });
  }

  removeFavorite(favorite: FamilyMemberFavorite): void {
    this.familyMembersService.deleteFavorite(favorite.familyMemberId, favorite.id).subscribe({
      next: () => this.loadFavorites(this.members()),
      error: () => this.snackBar.open('Could not remove the favorite', 'Dismiss', { duration: 4000 })
    });
  }

  startAdd(): void {
    this.editingId.set(null);
    this.form = emptyForm();
    this.formVisible.set(true);
  }

  startEdit(member: FamilyMember): void {
    this.editingId.set(member.id);
    this.form = {
      name: member.name,
      isGuest: member.isGuest,
      allergies: member.allergies ?? '',
      dietaryConstraints: member.dietaryConstraints ?? '',
      preferenceNotes: member.preferenceNotes ?? ''
    };
    this.formVisible.set(true);
  }

  cancelForm(): void {
    this.formVisible.set(false);
    this.editingId.set(null);
  }

  save(): void {
    const name = this.form.name.trim();
    if (!name) {
      return;
    }

    this.saving.set(true);
    const payload = {
      name,
      isGuest: this.form.isGuest,
      allergies: this.form.allergies.trim() || undefined,
      dietaryConstraints: this.form.dietaryConstraints.trim() || undefined,
      preferenceNotes: this.form.preferenceNotes.trim() || undefined
    };

    const editingId = this.editingId();
    const request = editingId
      ? this.familyMembersService.updateMember(editingId, { ...payload, isActive: true })
      : this.familyMembersService.createMember(payload);

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.formVisible.set(false);
        this.editingId.set(null);
        this.loadMembers();
      },
      error: () => {
        this.saving.set(false);
        this.snackBar.open('Could not save the member', 'Dismiss', { duration: 4000 });
      }
    });
  }

  remove(member: FamilyMember): void {
    this.familyMembersService.deleteMember(member.id).subscribe({
      next: () => {
        this.snackBar.open(`${member.name} removed`, 'Dismiss', { duration: 3000 });
        this.loadMembers();
      },
      error: () => this.snackBar.open('Could not remove the member', 'Dismiss', { duration: 4000 })
    });
  }
}
