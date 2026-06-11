import { ChangeDetectionStrategy, Component, OnInit, computed, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { StatisticsService } from '../../services/statistics.service';
import { FamilyMembersService } from '../../services/family-members.service';
import { DishStatistics, DishStatistic } from '../../models/statistics.model';
import { FamilyMember } from '../../models/family-member.model';

@Component({
  selector: 'app-statistics-page',
  standalone: true,
  imports: [
    DatePipe,
    DecimalPipe,
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatMenuModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatTableModule
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './statistics.page.html',
  styleUrl: './statistics.page.scss'
})
export class StatisticsPage implements OnInit {
  readonly statistics = signal<DishStatistics | null>(null);
  readonly members = signal<FamilyMember[]>([]);
  readonly loading = signal(true);
  readonly filter = signal('');

  readonly displayedColumns = ['dishName', 'timesPlanned', 'timesEaten', 'rating', 'lastPlanned', 'favorite'];

  readonly filteredDishes = computed<DishStatistic[]>(() => {
    const stats = this.statistics();
    if (!stats) {
      return [];
    }
    const term = this.filter().trim().toLowerCase();
    return term
      ? stats.dishes.filter(d => d.dishName.toLowerCase().includes(term))
      : stats.dishes;
  });

  constructor(
    private statisticsService: StatisticsService,
    private familyMembersService: FamilyMembersService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.statisticsService.getDishStatistics().subscribe({
      next: stats => {
        this.statistics.set(stats);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.snackBar.open('Could not load statistics', 'Dismiss', { duration: 4000 });
      }
    });

    this.familyMembersService.getMembers().subscribe({
      next: members => this.members.set(members),
      error: () => { /* favorite menu simply stays empty */ }
    });
  }

  /** Marks a dish from the history as a member's favorite */
  markAsFavorite(dish: DishStatistic, member: FamilyMember): void {
    this.familyMembersService.addFavorite(member.id, { dishName: dish.dishName }).subscribe({
      next: () => this.snackBar.open(
        `"${dish.dishName}" added to ${member.name}'s favorites`, 'Dismiss', { duration: 3000 }),
      error: () => this.snackBar.open('Could not add the favorite', 'Dismiss', { duration: 4000 })
    });
  }
}
