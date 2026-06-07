import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import { firstValueFrom } from 'rxjs';
import { WeekPlansService } from '../../services/week-plans.service';
import { RecipesService } from '../../services/recipes.service';
import { FamilyService } from '../../services/family.service';
import { DayOfWeek, IntentTag, MealSlot, MealType, RecipeSummary, WeekPlan, FamilyMember } from '../../models';

const DAY_ORDER: DayOfWeek[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
const MEAL_ORDER: MealType[] = ['Breakfast', 'Lunch', 'Dinner'];
const INTENT_TAGS: IntentTag[] = ['None', 'Meat', 'Vegetarian', 'Fish', 'Pasta', 'Soup', 'Salad', 'Other'];

@Component({
  selector: 'app-week-planner',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatButtonModule, MatIconModule, MatChipsModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="planner">
      <header class="planner__header">
        <h1>Week of {{ weekStart() }}</h1>
        <div class="planner__nav">
          <button mat-stroked-button (click)="shiftWeek(-7)"><mat-icon>chevron_left</mat-icon> Previous</button>
          <button mat-stroked-button (click)="shiftWeek(0)">This week</button>
          <button mat-stroked-button (click)="shiftWeek(7)">Next <mat-icon>chevron_right</mat-icon></button>
        </div>
      </header>

      @if (plan(); as p) {
        <div class="planner__grid">
          <div class="cell head"></div>
          @for (m of meals; track m) { <div class="cell head">{{ m }}</div> }

          @for (d of days; track d) {
            <div class="cell day-head">{{ d }}</div>
            @for (m of meals; track m) {
              @if (slotFor(p, d, m); as slot) {
                <div class="cell slot">
                  <mat-form-field appearance="outline" class="dense">
                    <mat-label>Recipe</mat-label>
                    <mat-select [ngModel]="slot.recipeId" (ngModelChange)="setRecipe(slot, $event)">
                      <mat-option [value]="null">— none —</mat-option>
                      @for (r of recipes(); track r.id) {
                        <mat-option [value]="r.id">{{ r.title }}</mat-option>
                      }
                    </mat-select>
                  </mat-form-field>
                  <mat-form-field appearance="outline" class="dense">
                    <mat-label>Or vague intent</mat-label>
                    <input matInput [ngModel]="slot.vagueIntent" (change)="setIntent(slot, $any($event.target).value)" />
                  </mat-form-field>
                  <mat-form-field appearance="outline" class="dense">
                    <mat-label>Tag</mat-label>
                    <mat-select [ngModel]="slot.intentTag" (ngModelChange)="setTag(slot, $event)">
                      @for (t of intentTags; track t) { <mat-option [value]="t">{{ t }}</mat-option> }
                    </mat-select>
                  </mat-form-field>
                  <mat-chip-set>
                    @for (a of slot.attendees; track a.id) {
                      @if (memberName(a.familyMemberId); as name) {
                        <mat-chip>{{ name }}</mat-chip>
                      }
                    }
                  </mat-chip-set>
                  <mat-form-field appearance="outline" class="dense">
                    <mat-label>Attendees</mat-label>
                    <mat-select multiple [ngModel]="attendeeIds(slot)" (ngModelChange)="setAttendees(slot, $event)">
                      @for (m of members(); track m.id) {
                        <mat-option [value]="m.id">{{ m.displayName }}</mat-option>
                      }
                    </mat-select>
                  </mat-form-field>
                </div>
              }
            }
          }
        </div>
      } @else {
        <p>Loading…</p>
      }
    </div>
  `,
  styles: [`
    .planner { padding: 16px; }
    .planner__header { display: flex; justify-content: space-between; align-items: center; margin-block-end: 16px; }
    .planner__nav button { margin-inline-start: 8px; }
    .planner__grid { display: grid; grid-template-columns: 110px repeat(3, 1fr); gap: 8px; }
    .cell { background: var(--mat-sys-surface-container); padding: 8px; border-radius: 6px; }
    .cell.head { font-weight: 600; text-align: center; background: var(--mat-sys-secondary-container); }
    .cell.day-head { font-weight: 600; align-self: center; }
    .cell.slot { display: flex; flex-direction: column; gap: 4px; }
    .dense { font-size: 0.85rem; }
  `],
})
export class WeekPlannerComponent {
  private readonly plansSvc = inject(WeekPlansService);
  private readonly recipesSvc = inject(RecipesService);
  private readonly familySvc = inject(FamilyService);

  readonly days = DAY_ORDER;
  readonly meals = MEAL_ORDER;
  readonly intentTags = INTENT_TAGS;

  private readonly _anchorDate = signal<Date>(this.toMonday(new Date()));
  readonly weekStart = computed(() => this._anchorDate().toISOString().slice(0, 10));

  readonly plan = signal<WeekPlan | null>(null);
  readonly recipes = signal<RecipeSummary[]>([]);
  readonly members = signal<FamilyMember[]>([]);

  constructor() {
    this.refresh();
    this.recipesSvc.list().subscribe((rs) => this.recipes.set(rs));
    this.familySvc.list().subscribe((ms) => this.members.set(ms));
  }

  shiftWeek(deltaDays: number) {
    if (deltaDays === 0) {
      this._anchorDate.set(this.toMonday(new Date()));
    } else {
      const next = new Date(this._anchorDate());
      next.setDate(next.getDate() + deltaDays);
      this._anchorDate.set(this.toMonday(next));
    }
    this.refresh();
  }

  private async refresh() {
    const plans = await firstValueFrom(this.plansSvc.forWeek(this.weekStart()));
    this.plan.set(plans[0] ?? null);
  }

  slotFor(plan: WeekPlan, day: DayOfWeek, meal: MealType): MealSlot | undefined {
    return plan.slots.find((s) => s.dayOfWeek === day && s.mealType === meal);
  }

  attendeeIds(slot: MealSlot): string[] {
    return slot.attendees.map((a) => a.familyMemberId).filter((x): x is string => !!x);
  }

  memberName(id?: string | null): string | null {
    return id ? this.members().find((m) => m.id === id)?.displayName ?? null : null;
  }

  async setRecipe(slot: MealSlot, recipeId: string | null) {
    slot.recipeId = recipeId;
    if (recipeId) slot.vagueIntent = null;
    await firstValueFrom(this.plansSvc.updateSlot(slot.weekPlanId, slot.id, {
      recipeId,
      vagueIntent: slot.vagueIntent,
      intentTag: slot.intentTag,
      frozenItemRef: slot.frozenItemRef,
      notes: slot.notes,
    }));
    await this.refresh();
  }

  async setIntent(slot: MealSlot, vagueIntent: string) {
    await firstValueFrom(this.plansSvc.updateSlot(slot.weekPlanId, slot.id, {
      recipeId: vagueIntent ? null : slot.recipeId,
      vagueIntent,
      intentTag: slot.intentTag,
      frozenItemRef: slot.frozenItemRef,
      notes: slot.notes,
    }));
    await this.refresh();
  }

  async setTag(slot: MealSlot, tag: IntentTag) {
    await firstValueFrom(this.plansSvc.updateSlot(slot.weekPlanId, slot.id, {
      recipeId: slot.recipeId,
      vagueIntent: slot.vagueIntent,
      intentTag: tag,
      frozenItemRef: slot.frozenItemRef,
      notes: slot.notes,
    }));
    await this.refresh();
  }

  async setAttendees(slot: MealSlot, ids: string[]) {
    await firstValueFrom(this.plansSvc.updateAttendees(slot.weekPlanId, slot.id, {
      familyMemberIds: ids,
      guestIds: [],
    }));
    await this.refresh();
  }

  private toMonday(d: Date): Date {
    const day = d.getDay(); // 0=Sun..6=Sat
    const offset = day === 0 ? -6 : 1 - day;
    const m = new Date(d);
    m.setDate(d.getDate() + offset);
    m.setHours(0, 0, 0, 0);
    return m;
  }
}
