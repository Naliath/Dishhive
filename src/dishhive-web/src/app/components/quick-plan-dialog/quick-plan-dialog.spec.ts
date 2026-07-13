import { describe, expect, it, vi } from 'vitest';
import { FamilyMember } from '../../models/family-member.model';
import { Course, MealType, PlannedMeal } from '../../models/planned-meal.model';
import { QuickPlanDialog, QuickPlanDialogData, buildQuickPlanDays } from './quick-plan-dialog';

describe('QuickPlanDialog', () => {
  it('builds seven consecutive days and keeps existing meals visible', () => {
    const meal = {
      id: 'meal-1',
      date: '2027-01-01',
      mealType: MealType.Dinner,
      course: Course.Main
    } as PlannedMeal;

    const days = buildQuickPlanDays('2026-12-29', [meal], '2026-12-29');

    expect(days).toHaveLength(7);
    expect(days.map(day => day.iso)).toEqual([
      '2026-12-29', '2026-12-30', '2026-12-31', '2027-01-01',
      '2027-01-02', '2027-01-03', '2027-01-04'
    ]);
    expect(days[0].isToday).toBe(true);
    expect(days[3].meals).toEqual([meal]);
  });

  it('plans the selected course for dinner with non-guest household members', () => {
    const dialogRef = { close: vi.fn() };
    const data: QuickPlanDialogData = {
      recipe: { id: 'recipe-1', title: 'Lasagne' },
      startDate: '2026-07-13',
      meals: [],
      members: [
        { id: 'member-1', isGuest: false },
        { id: 'guest-1', isGuest: true }
      ] as FamilyMember[]
    };
    const dialog = new QuickPlanDialog(data, dialogRef as never);
    dialog.course.set(Course.Dessert);

    dialog.plan(dialog.days[2]);

    expect(dialogRef.close).toHaveBeenCalledWith({
      date: '2026-07-15',
      mealType: MealType.Dinner,
      course: Course.Dessert,
      recipeId: 'recipe-1',
      familyMemberIds: ['member-1']
    });
  });
});
