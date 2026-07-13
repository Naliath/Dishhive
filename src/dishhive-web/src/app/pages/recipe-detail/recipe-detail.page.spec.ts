import { afterEach, describe, expect, it, vi } from 'vitest';
import { of } from 'rxjs';
import { Course, CreatePlannedMeal, MealType, PlannedMeal } from '../../models/planned-meal.model';
import { Recipe } from '../../models/recipe.model';
import { RecipeDetailPage } from './recipe-detail.page';

describe('RecipeDetailPage serving scaling', () => {
  afterEach(() => vi.useRealTimers());

  function createPage() {
    const measurementService = { format: vi.fn((quantity?: number, unit?: string) =>
      quantity == null ? '' : `${quantity} ${unit ?? ''}`.trim()) };
    const familyMembersService = { getMembers: vi.fn() };
    const plannedMealsService = { getMeals: vi.fn(), createMeal: vi.fn() };
    const statisticsService = { getDishStatistics: vi.fn(() => of({ dishes: [] })) };
    const dialog = { open: vi.fn() };
    const snackBar = { open: vi.fn() };
    const page = new RecipeDetailPage(
      null as never,
      null as never,
      null as never,
      null as never,
      measurementService as never,
      familyMembersService as never,
      plannedMealsService as never,
      statisticsService as never,
      dialog as never,
      snackBar as never
    );
    page.recipe.set({ servings: 4 } as Recipe);
    page.selectedServings.set(4);
    return {
      page,
      measurementService,
      familyMembersService,
      plannedMealsService,
      dialog,
      snackBar
    };
  }

  it('scales a structured quantity relative to the recipe servings', () => {
    const { page, measurementService } = createPage();

    page.adjustServings(2);

    expect(page.formatQuantity(200, 'g')).toBe('300 g');
    expect(measurementService.format).toHaveBeenCalledWith(300, 'g');
  });

  it('leaves ingredient lines without a quantity unchanged', () => {
    const { page, measurementService } = createPage();
    page.adjustServings(2);

    expect(page.formatQuantity(undefined, 'piece')).toBe('');
    expect(measurementService.format).toHaveBeenCalledWith(undefined, 'piece');
  });

  it('keeps the serving count between one and one hundred', () => {
    const { page } = createPage();

    page.adjustServings(-10);
    expect(page.selectedServings()).toBe(1);

    page.adjustServings(200);
    expect(page.selectedServings()).toBe(100);
  });

  it('switches away from unscalable verbatim values when servings change', () => {
    const { page } = createPage();
    page.showOriginalIngredients.set(true);

    page.adjustServings(1);

    expect(page.showOriginalIngredients()).toBe(false);
  });

  it('only exposes original comparisons when source content differs', () => {
    const { page } = createPage();
    page.recipe.set({
      servings: 4,
      ingredients: [
        { id: '1', name: 'butter', quantity: 200, unit: 'g', originalText: '200 g boter' },
        { id: '2', name: 'salt', quantity: 1, unit: 'tsp', originalText: '1 tsp salt' }
      ],
      steps: [
        { id: '1', stepNumber: 1, instruction: 'Melt the butter.', originalInstruction: 'Smelt de boter.' },
        { id: '2', stepNumber: 2, instruction: 'Serve.', originalInstruction: 'Serve.' }
      ]
    } as Recipe);

    expect(page.hasOriginalIngredients()).toBe(true);
    expect(page.originalIngredientText(page.recipe()!.ingredients[0])).toBe('200 g boter');
    expect(page.originalIngredientText(page.recipe()!.ingredients[1])).toBeNull();
    expect(page.hasOriginalSteps()).toBe(true);
    expect(page.originalStepInstruction(page.recipe()!.steps[0])).toBe('Smelt de boter.');
    expect(page.originalStepInstruction(page.recipe()!.steps[1])).toBeNull();
  });

  it('loads occupied dates and creates the meal returned by quick planning', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(2026, 6, 13, 12));
    const {
      page,
      familyMembersService,
      plannedMealsService,
      dialog,
      snackBar
    } = createPage();
    const existingMeal = {
      id: 'existing-meal',
      date: '2026-07-14',
      mealType: MealType.Dinner,
      course: Course.Main
    } as PlannedMeal;
    const result: CreatePlannedMeal = {
      date: '2026-07-15',
      mealType: MealType.Dinner,
      course: Course.Dessert,
      recipeId: 'recipe-1',
      familyMemberIds: ['member-1']
    };
    page.recipe.set({ id: 'recipe-1', title: 'Tiramisu', servings: 4 } as Recipe);
    familyMembersService.getMembers.mockReturnValue(of([{ id: 'member-1', isGuest: false }]));
    plannedMealsService.getMeals.mockReturnValue(of([existingMeal]));
    plannedMealsService.createMeal.mockReturnValue(of({ id: 'new-meal', ...result }));
    dialog.open.mockReturnValue({ afterClosed: () => of(result) });

    page.openQuickPlan();

    expect(plannedMealsService.getMeals).toHaveBeenCalledWith('2026-07-13', '2026-07-19');
    expect(dialog.open).toHaveBeenCalledWith(expect.any(Function), {
      data: expect.objectContaining({
        recipe: { id: 'recipe-1', title: 'Tiramisu' },
        startDate: '2026-07-13',
        meals: [existingMeal]
      })
    });
    expect(plannedMealsService.createMeal).toHaveBeenCalledWith(result);
    expect(snackBar.open).toHaveBeenCalledWith(
      expect.stringContaining('Planned Tiramisu'),
      'Dismiss',
      { duration: 3500 }
    );
    expect(page.quickPlanLoading()).toBe(false);
  });
});
