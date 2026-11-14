import { expect, Page, test } from '@playwright/test';
import type { Recipe } from '../src/app/models/recipe.model';
import type { FamilyMember } from '../src/app/models/family-member.model';
import type { DishStatistics } from '../src/app/models/statistics.model';
import type { ShoppingList } from '../src/app/models/shopping-list.model';
import type { MealSuggestion, MealSuggestions } from '../src/app/models/meal-suggestion.model';
import type { PlannedMeal } from '../src/app/models/planned-meal.model';
import type { AiPlanningMetricsResponse } from '../src/app/models/integration-status.model';
import type { AiModelTestStatus } from '../src/app/models/integration-status.model';

const runtimeErrors = new WeakMap<Page, string[]>();

const recipeId = '11111111-1111-1111-1111-111111111111';
const memberId = '22222222-2222-2222-2222-222222222222';
const recipe: Recipe = {
  id: recipeId,
  title: 'A deliberately long family recipe title that must wrap without crowding its controls',
  description: 'Fixture recipe for a read-only browser regression run.',
  servings: 4,
  category: 'Main',
  tags: [],
  cookbookIds: [],
  hasLocalImage: false,
  totalTimeMinutes: 25,
  ingredients: [{ id: '33333333-3333-3333-3333-333333333333', name: 'tomatoes', quantity: 2, unit: 'piece', sortOrder: 0, originalText: '2 tomatoes' }],
  steps: [{ id: '44444444-4444-4444-4444-444444444444', stepNumber: 1, instruction: 'Cook gently.' }],
  dietaryFacts: { contains: [], status: 2, recipeRevision: 1, assessedRevision: 1, isCurrent: true, history: [] },
  createdAt: '2026-09-01T12:00:00Z',
  updatedAt: '2026-09-01T12:00:00Z'
};

async function installReadOnlyFixtures(page: Page): Promise<void> {
  await page.clock.setFixedTime(new Date('2026-09-13T12:00:00Z'));
  const errors: string[] = [];
  runtimeErrors.set(page, errors);
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error' && !message.text().startsWith('Failed to load resource'))
      errors.push(message.text());
  });
  await page.addInitScript(() => {
    localStorage.setItem('dishhive.onboarding.resolved.v1', 'true');
  });
  await page.route('**/api/**', async route => {
    const request = route.request();
    const path = new URL(request.url()).pathname.toLowerCase();
    if (request.method() !== 'GET') {
      await route.fulfill({ status: 405, json: { title: 'Fixture mode is read-only' } });
      return;
    }
    let body: unknown;
    const headers: Record<string, string> = {};
    if (path === `/api/recipes/${recipeId}`) body = recipe;
    else if (path === '/api/recipes' || path === '/api/recipes/page') {
      body = path.endsWith('/page') ? { items: [recipe], totalCount: 1, page: 1, pageSize: 12 } : [recipe];
      headers['Access-Control-Expose-Headers'] = 'X-Total-Count';
      headers['X-Total-Count'] = '1';
    } else if (path.includes('/facts/status')) {
      body = { unassessed: 0, aiDetected: 0, userConfirmed: 1, stale: 0, pending: 0, failed: 0, queueDepth: 0, running: false, available: false };
    } else if (path === '/api/familymembers') {
      body = [{ id: memberId, name: 'Alexandra With An Exceptionally Long Household Display Name', isActive: true, isGuest: false,
        allergyTags: [], dietTags: [], createdAt: recipe.createdAt, updatedAt: recipe.updatedAt }] satisfies FamilyMember[];
    } else if (path === `/api/familymembers/${memberId}/favorites`) {
      body = [{ id: '55555555-5555-5555-5555-555555555555', familyMemberId: memberId, recipeId, dishName: recipe.title }];
    } else if (path.includes('/integrations/ai/planning-runs')) {
      const metrics = {
        summary: { runCount: 0, successfulRuns: 0, partialFallbackRuns: 0, fallbackRuns: 0, averageTotalDurationMs: 0, averageCompletionDurationMs: 0, p95TotalDurationMs: 0, totalInputTokens: 0, totalOutputTokens: 0, totalReasoningTokens: 0, totalSearches: 0, totalEmptySearches: 0, totalRecipeResolutions: 0, totalResolutionFailures: 0, totalParseFailures: 0 },
        runs: [],
        taskMetrics: { runCount: 12, successfulRuns: 9, failedRuns: 2, cancelledRuns: 1, incompleteRuns: 0, averageDurationMs: 3200, p95DurationMs: 7100, totalInputTokens: 12500, totalOutputTokens: 3500, totalReasoningTokens: 0, tasks: [
          { task: 'recipe.facts', provider: 'fixture', model: 'representative-model', configFingerprint: 'fixture-v1', runCount: 8, successfulRuns: 7, failedRuns: 1, averageDurationMs: 2300, p95DurationMs: 6100, totalTokens: 10000 },
          { task: 'recipe.translate', provider: 'fixture', model: 'representative-model', configFingerprint: 'fixture-v1', runCount: 4, successfulRuns: 2, failedRuns: 1, averageDurationMs: 5000, p95DurationMs: 7100, totalTokens: 6000 }
        ] },
        calls: [{
          id: '66666666-6666-6666-6666-666666666666', operationId: 'translation-of-a-large-imported-recipe',
          task: 'recipe.translate', provider: 'fixture', model: 'representative-model-with-a-long-name',
          configFingerprint: 'fixture-v1', contractVersion: 'recipe-translation-v1',
          startedAt: '2026-09-13T12:00:00Z',
          outcome: 'validationFailure', error: 'The translated recipe omitted one of the original steps. The saved recipe is available for review.',
          responseMode: 'jsonSchema', finishReason: 'stop', attempt: 2, operationAttempt: 5, maxOutputTokens: 8000,
          temperature: 0, reasoningDisabled: true, schemaRequested: true, schemaApplied: true,
          promptCharacters: 6000, inputBudgetTokens: 12000, queueWaitMs: 1200, promptHash: 'fixture-hash',
          outputCharacters: 3200, inputTokens: 2400, outputTokens: 1200, reasoningTokens: 0,
          totalTokens: 3600, durationMs: 7100
        }]
      } satisfies AiPlanningMetricsResponse;
      const selectedTask = new URL(request.url()).searchParams.get('task');
      const selectedSummary = metrics.taskMetrics.tasks.find(task => task.task === selectedTask);
      if (selectedTask !== null && !selectedSummary) {
        errors.push(`Unexpected metrics task filter: ${selectedTask}`);
        await route.fulfill({ status: 400, json: { title: 'Unexpected metrics task filter' } });
        return;
      }
      const inputTokens = selectedTask === 'recipe.facts' ? 7500 : 5000;
      body = selectedSummary ? {
        ...metrics,
        taskMetrics: {
          ...metrics.taskMetrics,
          runCount: selectedSummary.runCount,
          successfulRuns: selectedSummary.successfulRuns,
          failedRuns: selectedSummary.failedRuns,
          cancelledRuns: selectedSummary.runCount - selectedSummary.successfulRuns - selectedSummary.failedRuns,
          averageDurationMs: selectedSummary.averageDurationMs,
          p95DurationMs: selectedSummary.p95DurationMs,
          totalInputTokens: inputTokens,
          totalOutputTokens: selectedSummary.totalTokens - inputTokens,
          tasks: [selectedSummary]
        },
        calls: metrics.calls.filter(call => call.task === selectedTask)
      } : metrics;
    } else if (path.includes('/integrations/status')) {
      body = {
        freezy: { configured: false, reachable: false },
        scraper: { configured: false, reachable: false },
        webSearch: { configured: false, reachable: false, operational: false },
        ai: { configured: false, reachable: false, modelTestVerdict: 'notTested' }
      };
    } else if (path.includes('/freezer/suggestions')) body = { enabled: false, items: [] };
    else if (path.includes('/suggestions/status')) body = { enabled: true };
    else if (path === '/api/statistics/dishes') body = { dishes: [
      { dishName: recipe.title, timesPlanned: 12, lastPlanned: '2026-09-10', timesEaten: 10, averageRating: 4.5, lovedCount: 8 }
    ], unspecifiedCount: 1 } satisfies DishStatistics;
    else if (path === '/api/shoppinglist') body = {
      from: '2026-09-07', to: '2026-09-13',
      items: [{ name: 'Tomatoes', quantity: 2, unit: 'piece', sourceRecipes: [recipe.title] }], reminders: []
    } satisfies ShoppingList;
    else if (path === '/api/settings/languages') body = [{ code: 'en', displayName: 'English' }, { code: 'nl', displayName: 'Nederlands' }];
    else if (path === '/api/settings/preferences') body = {
      measurementSystem: 0, firstDayOfWeek: 1, preferredLanguage: 'en', translateImportedRecipes: false,
      supportedLanguages: [{ code: 'en', displayName: 'English' }, { code: 'nl', displayName: 'Nederlands' }]
    };
    else if (path === '/api/settings/ai-prompt') body = { editablePrompt: '', defaultPrompt: '', isOverridden: false, fixedPrompt: '' };
    else if (path === '/api/integrations/ai/test') body = { state: 'notConfigured', result: null };
    else if (['/api/plannedmeals', '/api/cookbooks', '/api/cookbooks/auto-collections', '/api/recipetags',
      '/api/dietarytags', '/api/recipes/categories', '/api/recipes/ingredients', '/api/recipes/sources'].includes(path)) body = [];
    else {
      errors.push(`Missing API fixture: ${request.method()} ${path}`);
      await route.fulfill({ status: 501, json: { title: 'Missing API fixture' } });
      return;
    }
    await route.fulfill({ status: 200, json: body, headers });
  });
}

async function assertGeometry(page: Page): Promise<void> {
  const geometry = await page.evaluate(() => {
    const viewport = window.innerWidth;
    const outside = [...document.querySelectorAll<HTMLElement>('body *')].filter(element => {
      if (element.closest('.mat-drawer:not(.mat-drawer-opened)')) return false;
      for (let parent = element.parentElement; parent; parent = parent.parentElement) {
        const overflow = getComputedStyle(parent).overflowX;
        if (overflow === 'auto' || overflow === 'scroll') return false;
      }
      const style = getComputedStyle(element);
      const rect = element.getBoundingClientRect();
      return style.display !== 'none' && style.visibility !== 'hidden' && rect.width > 0
        && (rect.left < -1 || rect.right > viewport + 1);
    });
    return { documentWidth: document.documentElement.scrollWidth, viewport, outside: outside.length };
  });
  expect(geometry.documentWidth).toBeLessThanOrEqual(geometry.viewport + 1);
  expect(geometry.outside).toBe(0);
}

test.beforeEach(async ({ page }) => installReadOnlyFixtures(page));
test.afterEach(async ({ page }) => expect(runtimeErrors.get(page) ?? []).toEqual([]));

for (const width of [360, 1280]) {
  for (const language of ['en', 'nl'] as const) {
    test(`normalized culinary units follow ${language} at ${width}px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await page.route('**/api/settings/preferences', route => route.fulfill({ json: {
        measurementSystem: 0, firstDayOfWeek: 1, preferredLanguage: language,
        translateImportedRecipes: false, supportedLanguages: []
      } }));
      const ingredients = [
        { id: 'spoon', name: 'olive oil', quantity: 2, unit: 'el', sortOrder: 0, originalText: '2 eetlepels olijfolie' },
        { id: 'teaspoon', name: 'salt', quantity: 0.5, unit: 'tl', sortOrder: 1 },
        { id: 'leaves', name: 'basil', quantity: 3, unit: 'blaadje', sortOrder: 2 }
      ];
      await page.route(`**/api/recipes/${recipeId}`, route => route.fulfill({ json: { ...recipe, ingredients } }));
      await page.route('**/api/shoppinglist?**', route => route.fulfill({ json: {
        from: '2026-09-07', to: '2026-09-13', reminders: [],
        items: ingredients.map(ingredient => ({ ...ingredient, sourceRecipes: [] }))
      } satisfies ShoppingList }));
      const expected = language === 'en'
        ? ['2 tbsp', '0.5 tsp', '3 leaves'] : ['2 el', '0.5 tl', '3 blaadjes'];

      await page.goto(`/recipes/${recipeId}`);
      await expect(page.locator('.ingredients li > span > strong')).toHaveText(expected);
      await page.getByRole('switch').click();
      await expect(page.locator('.original-value')).toContainText('2 eetlepels olijfolie');
      await assertGeometry(page);

      await page.goto('/shopping-list');
      await expect(page.locator('.item-quantity')).toHaveText(expected);
      await assertGeometry(page);
      await page.evaluate(() => {
        Object.defineProperty(navigator, 'clipboard', { configurable: true, value: {
          writeText: async (value: string) => { document.body.dataset['copiedText'] = value; }
        } });
      });
      await page.getByRole('button', { name: language === 'en' ? 'Copy as text' : 'Als tekst kopiëren' }).click();
      for (const [index, ingredient] of ingredients.entries()) {
        await expect(page.locator('body')).toHaveAttribute('data-copied-text', new RegExp(`- ${expected[index]} ${ingredient.name}`));
      }
    });
  }

  test(`Dutch labels and long collection names fit at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.route('**/api/settings/preferences', route => route.fulfill({ json: {
      measurementSystem: 0, firstDayOfWeek: 0, preferredLanguage: 'nl', translateImportedRecipes: false,
      householdTimeZone: 'Europe/Brussels', supportedLanguages: [{ code: 'en', displayName: 'English' }, { code: 'nl', displayName: 'Nederlands' }]
    } }));
    const longName = 'My own collection name with a very long description that remains verbatim and accessible';
    await page.route('**/api/cookbooks', route => route.fulfill({ json: [
      { id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', name: longName, kind: 'manual', recipeCount: 100 },
      { id: 'auto-quick', name: 'Quick (max 30 min)', kind: 'auto', recipeCount: 1 }
    ] }));
    await page.goto('/recipes');
    await expect(page.getByRole('button', { name: 'Importeren via URL' })).toBeVisible();
    await expect(page.getByRole('option', { name: new RegExp(longName) })).toBeVisible();
    await expect(page.getByRole('option', { name: /Snel/ })).toBeVisible();
    await expect(page.locator('mat-paginator')).toContainText('Items per pagina:');
    await assertGeometry(page);
    await expect(page).toHaveScreenshot(`collections-nl-${width}.png`);
    await page.goto(`/recipes/${recipeId}`);
    await page.getByRole('button', { name: 'Nu plannen!' }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog).toContainText('Hoofdgerecht');
    await expect(dialog).toContainText('Vandaag');
    await expect(dialog).toContainText('Nog niets gepland');
    await assertGeometry(page);
    await expect(page).toHaveScreenshot(`quick-plan-nl-${width}.png`);
    await dialog.locator('.date-option').last().scrollIntoViewIfNeeded();
    await expect(dialog.locator('.date-option').last()).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(page.getByRole('button', { name: 'Nu plannen!' })).toBeFocused();
  });

  test(`quick planning and collection controls support keyboard review at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    let deleted = false;
    const collectionId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
    await page.route('**/api/cookbooks', route => route.fulfill({ json: deleted ? [] : [
      { id: collectionId, name: 'Easy Weekday Dishes', kind: 'manual', recipeCount: 1 },
      { id: 'auto-quick', name: 'Quick (max 30 min)', kind: 'auto', recipeCount: 1 }
    ] }));
    await page.route(`**/api/cookbooks/${collectionId}`, route => {
      deleted = true;
      return route.fulfill({ status: 204 });
    });
    await page.goto('/recipes');
    await expect(page.locator('.cookbook-chip').first()).toBeVisible();
      const collection = page.getByRole('option', { name: /Easy Weekday Dishes/ });
      await collection.focus();
      await page.keyboard.press('Space');
      await expect(collection).toHaveAttribute('aria-selected', 'true');
      await assertGeometry(page);
      await expect(page).toHaveScreenshot(`collections-${width}.png`);
      await page.keyboard.press('Delete');
      await expect(page.getByRole('option', { name: /Easy Weekday Dishes/ })).toHaveCount(0);
    await page.goto(`/recipes/${recipeId}`);
    await page.getByRole('button', { name: 'Plan now!' }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.locator('.date-option')).toHaveCount(7);
    await dialog.locator('.date-option').first().focus();
      await expect(dialog.locator('.date-option').first()).toBeFocused();
      await assertGeometry(page);
      await expect(page).toHaveScreenshot(`quick-plan-${width}.png`);
      await page.keyboard.press('Escape');
      await expect(dialog).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Plan now!' })).toBeFocused();
    let plannedDate = '';
    await page.route('**/api/plannedmeals', async route => {
      if (route.request().method() !== 'POST') return route.fulfill({ json: [] });
      const meal = route.request().postDataJSON();
      plannedDate = meal.date;
      return route.fulfill({ status: 201, json: { ...meal, id: 'keyboard-meal', attendeeIds: [], ratings: [] } });
    });
    await page.getByRole('button', { name: 'Plan now!' }).click();
    await dialog.locator('.date-option').first().focus();
    await page.keyboard.press('Enter');
    await expect.poll(() => plannedDate).toBe('2026-09-13');
  });
}

for (const width of [360, 1280]) {
  test(`review telemetry distinguishes edits from changed inputs at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    const response = page.waitForResponse(response => response.url().includes('/api/integrations/ai/planning-runs'));
    await page.goto('/settings/ai-stats');
    const data: AiPlanningMetricsResponse = await (await response).json();
    data.calls[0].inputRevisions = [{ kind: 'recipe', id: recipeId, revision: 7, confirmationVersion: 3, fingerprint: 'a'.repeat(64) }];
    data.calls[0].inputFingerprint = 'b'.repeat(64);
    data.reviews = {
      reviewActions: 2, factsReviews: 1, factsCorrections: 1, textEditsAfterTranslation: 0,
      reviewedSuggestions: 1, editedSuggestions: 0, suggestionsWithChangedInputs: 1,
      events: [
        { id: 'review-facts', occurredAt: '2026-09-13T12:00:00Z', task: 'recipe.facts', kind: 'factsCorrected',
          batchId: null, suggestionId: null, outcome: null,
          recipeId, operationId: 'source', sourceRevision: 7, observedRevision: 7,
          sourceConfirmationVersion: 3, observedConfirmationVersion: 4, changedFields: ['facts'], inputChanges: [] },
        { id: 'review-input', occurredAt: '2026-09-13T12:01:00Z', task: 'planning.generate', kind: 'suggestionReviewed',
          operationId: null, batchId: null, suggestionId: null, sourceConfirmationVersion: null, observedConfirmationVersion: null,
          recipeId, outcome: 'needsReview', sourceRevision: 7, observedRevision: 8,
          changedFields: [], inputChanges: ['recipeRevision'] }
      ]
    };
    await page.route('**/api/integrations/ai/planning-runs**', route => route.fulfill({ json: data }));
    await page.reload();
    const reviews = page.locator('section[aria-labelledby="review-summary-title"]');
    await expect(reviews).toContainText('not accuracy scores');
    await reviews.getByText('Show review history', { exact: true }).click();
    await expect(reviews).toContainText('AI facts corrected on the same source');
    await expect(reviews).toContainText('Changed inputs: recipe version');
    await assertGeometry(page);
    await reviews.getByRole('heading', { name: 'Review signals', exact: true }).evaluate(element => element.scrollIntoView({ block: 'start' }));
    await expect(page).toHaveScreenshot(`review-signals-${width}.png`);
    await expect(reviews.locator('.run-list')).toHaveScreenshot(`review-history-${width}.png`);
    const call = page.locator('.run-card').first();
    await call.getByText('Operation input snapshot (1)', { exact: true }).click();
    await expect(call).toContainText('Recipe revision 7');
    await expect(call).toContainText('Human confirmation version 3');
    await assertGeometry(page);
    await expect(call).toHaveScreenshot(`input-revisions-${width}.png`);
  });
}

for (const width of [360, 1280]) {
  test(`freezer reservation review retains meals and handles changed stock at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    const date = '2026-09-13';
    const mealId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
    let released = false;
    let resolved = false;
    let state = 'ready';
    let issue = 'shortage';
    const changes: unknown[] = [];
    await page.route('**/api/freezer/**', async route => {
      const request = route.request();
      const path = new URL(request.url()).pathname;
      if (path.endsWith('/suggestions')) return route.fulfill({ json: { enabled: true, items: [] } });
      if (request.method() === 'PUT') {
        const change = request.postDataJSON();
        changes.push(change);
        released = change.action === 'release';
        resolved = true;
        return route.fulfill({ status: 204 });
      }
      return route.fulfill({ json: { state, checkedAt: '2026-09-13T12:00:00Z', issues: resolved || state !== 'ready' ? [] : [{
        itemRef: 'stock', name: 'A very long freezer item description that remains readable while reviewing reservations',
        fingerprint: 'current', stockQuantity: issue === 'shortage' ? 1 : 3,
        reservedQuantity: 2, shortage: issue === 'shortage' ? 1 : 0, issue,
        meals: [{ id: mealId, date, dishName: 'Retained soup dinner', quantity: 2 }]
      }] } });
    });
    await page.route('**/api/plannedmeals?**', route => route.fulfill({ json: [{
      id: mealId, date, mealType: 2, course: 0, dishName: 'Retained soup dinner',
      freezyItemRef: released ? null : 'stock', freezyItemQuantity: released ? 0 : 2, attendeeIds: [], ratings: []
    }] }));
    await page.goto('/');
    await page.getByRole('button', { name: 'Freezer links', exact: true }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog).toContainText('There is less stock');
    await assertGeometry(page);
    await expect(page).toHaveScreenshot(`freezer-reservations-shortage-${width}.png`);
    issue = 'changed';
    await dialog.getByRole('button', { name: 'Check again' }).click();
    await expect(dialog).toContainText('The item details changed');
    await expect(dialog.getByRole('button', { name: 'Confirm item' })).toBeVisible();
    await assertGeometry(page);
    await expect(page).toHaveScreenshot(`freezer-reservations-changed-${width}.png`);
    await dialog.getByRole('button', { name: 'Confirm item' }).click();
    await expect(dialog).toContainText('All future freezer reservations match');
    issue = 'shortage';
    resolved = false;
    await dialog.getByRole('button', { name: 'Check again' }).click();
    await expect(dialog).toContainText('There is less stock');
    await dialog.getByRole('button', { name: 'Unlink freezer' }).click();
    await expect(dialog).toContainText('All future freezer reservations match');
    expect(changes).toEqual([
      { itemRef: 'stock', quantity: 2, action: 'confirm', fingerprint: 'current' },
      { itemRef: 'stock', quantity: 2, action: 'release', fingerprint: 'current' }
    ]);
    state = 'unavailable';
    await dialog.getByRole('button', { name: 'Check again' }).click();
    await expect(dialog).toContainText('Existing reservations have been retained');
    await expect(dialog.getByRole('button', { name: 'Unlink freezer' })).toHaveCount(0);
    await dialog.getByRole('button', { name: 'Close', exact: true }).click();
    await expect(page.getByText('Retained soup dinner', { exact: true })).toBeVisible();
  });
}

for (const [name, route] of [
  ['week-planner', '/'], ['recipes', '/recipes'], ['recipe-create', '/recipes/new'],
  ['recipe-detail', `/recipes/${recipeId}`], ['recipe-edit', `/recipes/${recipeId}/edit`],
  ['family', '/family'], ['statistics', '/statistics'], ['shopping-list', '/shopping-list'],
  ['settings', '/settings'], ['ai-statistics', '/settings/ai-stats']
] as const) {
  test(`${name} is usable at 360px`, async ({ page }) => {
    await page.goto(route);
    await page.waitForLoadState('networkidle');
    if (name === 'statistics') await expect(page.getByRole('cell', { name: recipe.title, exact: true })).toBeVisible();
    if (name === 'shopping-list') await expect(page.getByText('Tomatoes', { exact: true })).toBeVisible();
    if (name === 'ai-statistics') await expect(page.getByText('Recipe translation', { exact: true }).first()).toBeVisible();
    if (name === 'ai-statistics') await expect(page.getByText('Metrics across 12 retained calls; the latest 1 are listed below.')).toBeVisible();
    if (name === 'settings') await expect(page.getByRole('combobox').first()).toContainText('English');
    await assertGeometry(page);
    await expect(page.locator('body')).not.toContainText('NaN');
    await expect(page.locator('body')).not.toContainText(/\{\w+\}/);
    await expect(page).toHaveScreenshot(`${name}-360.png`, { fullPage: true });
  });
}

test('populated AI call remains readable at 360px', async ({ page }) => {
  await page.goto('/settings/ai-stats');
  const call = page.locator('.run-card').first();
  await call.scrollIntoViewIfNeeded();
  await expect(call).toContainText('The translated recipe omitted');
  await assertGeometry(page);
  await expect(page).toHaveScreenshot('ai-statistics-call-360.png');
});

test('AI statistics keeps real counts through all tasks, one task, and all tasks again', async ({ page }) => {
  const taskFilters: (string | null)[] = [];
  page.on('request', request => {
    if (new URL(request.url()).pathname === '/api/integrations/ai/planning-runs')
      taskFilters.push(new URL(request.url()).searchParams.get('task'));
  });

  const initialRequest = page.waitForRequest(request => request.url().includes('/api/integrations/ai/planning-runs'));
  await page.goto('/settings/ai-stats');
  expect(new URL((await initialRequest).url()).searchParams.has('task')).toBe(false);
  await expect(page.getByText('Metrics across 12 retained calls; the latest 1 are listed below.')).toBeVisible();

  const translation = page.getByRole('button', { name: 'Recipe translation', exact: true });
  await translation.click();
  await expect(page.getByText('Metrics across 4 retained calls; the latest 1 are listed below.')).toBeVisible();
  await expect(translation).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('.run-card')).toHaveCount(1);

  await page.getByRole('button', { name: 'Show all tasks', exact: true }).click();
  await expect(page.getByText('Metrics across 12 retained calls; the latest 1 are listed below.')).toBeVisible();
  await expect(translation).toHaveAttribute('aria-pressed', 'false');
  await expect(page.locator('.task-filter')).toHaveCount(2);
  expect(taskFilters).toEqual([null, 'recipe.translate', null]);
});

test('suggestion dialog fits and closes with the keyboard', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: /suggest week/i }).click();
  const dialog = page.locator('.mat-mdc-dialog-surface');
  await expect(dialog).toBeVisible();
  await assertGeometry(page);
  await expect(page).toHaveScreenshot('suggestion-compose-360.png', { fullPage: true });
  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
});

test('representative desktop dark surface retains its hierarchy', async ({ browser }) => {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, colorScheme: 'dark', timezoneId: 'UTC', locale: 'en-US' });
  const page = await context.newPage();
  await installReadOnlyFixtures(page);
  await page.addInitScript(() => localStorage.setItem('dishhive-theme', 'dark'));
  await page.goto('/settings/ai-stats');
  await page.waitForLoadState('networkidle');
  await assertGeometry(page);
  await expect(page).toHaveScreenshot('ai-statistics-1280-dark.png', { fullPage: true });
  expect(runtimeErrors.get(page) ?? []).toEqual([]);
  await context.close();
});

for (const failure of ['importFailed', 'needsReview', 'transport'] as const) {
  test(`planning acceptance preserves the review after ${failure}`, async ({ page }) => {
    const meals: PlannedMeal[] = [];
    const submissions: string[][] = [];
    let suggestions: MealSuggestion[] = [];
    await page.route('**/api/plannedmeals**', async route => {
      const request = route.request();
      const path = new URL(request.url()).pathname;
      if (request.method() === 'GET' && path === '/api/plannedmeals') {
        await route.fulfill({ json: meals });
      } else if (request.method() === 'POST' && path === '/api/plannedmeals/suggestions') {
        const date = request.postDataJSON().selectedDates[0];
        suggestions = [
          { id: '66666666-6666-6666-6666-666666666666', date, mealType: 2, course: 0,
            dishName: 'Reviewed tomato soup', attendeeIds: [memberId], attendeeNames: [], freezyItemQuantity: 0,
            fromFallback: false, constraintIds: [], containsClasses: [], factsAssessed: true },
          { id: '77777777-7777-7777-7777-777777777777', date, mealType: 2, course: 3,
            dishName: 'A long dessert name that remains visible after a failed import', attendeeIds: [memberId], attendeeNames: [],
            sourceUrl: 'https://example.test/dessert', sourceName: 'Recipe source', freezyItemQuantity: 0,
            fromFallback: false, constraintIds: [], containsClasses: [], factsAssessed: true }
        ];
        if (failure === 'importFailed') suggestions.push({
          id: '99999999-9999-9999-9999-999999999999', date, mealType: 2, course: 1,
          dishName: 'An unselected appetizer', attendeeIds: [memberId], attendeeNames: [], freezyItemQuantity: 0,
          fromFallback: false, constraintIds: [], containsClasses: [], factsAssessed: true
        });
        await route.fulfill({ json: { batchId: '88888888-8888-8888-8888-888888888888', enabled: true,
          status: 'complete', unmetRequirements: [], excludedForAllergies: 0, suggestions } satisfies MealSuggestions });
      } else if (request.method() === 'POST' && path.endsWith('/suggestions/accept')) {
        const selected = request.postDataJSON().suggestions as MealSuggestion[];
        submissions.push(selected.map(item => item.id));
        if (failure === 'transport' && submissions.length === 1) {
          await route.fulfill({ status: 503, json: { title: 'Temporary failure' } });
          return;
        }
        await route.fulfill({ json: { results: selected.map((item, index) => {
          const failed = submissions.length === 1 && index === 1;
          if (!failed) meals.push({ ...item, ratings: [] });
          return { suggestionId: item.id, dishName: item.dishName,
            status: failed ? failure : 'created',
            recipeId: failed && failure === 'needsReview' ? recipeId : undefined,
            error: failed ? 'The recipe needs attention before it can be planned.' : undefined };
        }) } });
      } else await route.fallback();
    });
    await page.goto('/');
    await page.getByRole('button', { name: /suggest week/i }).click();
    await page.getByRole('button', { name: /^generate/i }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByText(suggestions[0]?.dishName ?? 'Reviewed tomato soup', { exact: true })).toBeVisible();
    if (failure === 'importFailed') await dialog.getByRole('checkbox').nth(2).uncheck();
    await dialog.getByRole('button', { name: 'Add 2 to plan' }).click();
    if (failure === 'needsReview') {
      await expect(dialog.getByRole('link', { name: 'Review saved recipe' })).toHaveAttribute('href', `/recipes/${recipeId}`);
      await expect(dialog.getByRole('checkbox')).toBeDisabled();
      await assertGeometry(page);
      await expect(page).toHaveScreenshot('suggestion-needs-review-360.png', { fullPage: true });
      await dialog.getByRole('button', { name: 'Finish review' }).click();
      expect(submissions[1]).toEqual([]);
    } else {
      if (failure === 'transport') await expect(dialog.getByRole('alert')).toContainText('You can retry');
      else await expect(dialog.getByRole('status')).toContainText('needs attention');
      await dialog.getByRole('button', { name: failure === 'transport' ? 'Add 2 to plan' : 'Add 1 to plan' }).click();
      expect(submissions[1]).toEqual(failure === 'transport' ? submissions[0] : [suggestions[1].id]);
    }
    await expect(dialog).toBeHidden();
    await expect(page.getByText('Reviewed tomato soup', { exact: true })).toHaveCount(1);
  });
}

test('recipe edit retains entered data after a failed save', async ({ page }) => {
  let saved = { ...recipe };
  let attempts = 0;
  await page.route(`**/api/recipes/${recipeId}`, async route => {
    if (route.request().method() === 'PUT') {
      attempts++;
      if (attempts === 1) await route.fulfill({ status: 503, json: { title: 'Save failed; retry is available' } });
      else {
        saved = { ...saved, ...route.request().postDataJSON() };
        await route.fulfill({ json: saved });
      }
    } else await route.fulfill({ json: saved });
  });
  await page.goto(`/recipes/${recipeId}/edit`);
  await page.getByRole('textbox', { name: /^title/i }).fill('Edited soup kept after a failed save');
  await page.getByRole('button', { name: /^save/i }).click();
  await expect(page.getByRole('textbox', { name: /^title/i })).toHaveValue('Edited soup kept after a failed save');
  await expect(page.getByRole('button', { name: /^save/i })).toBeEnabled();
  await page.getByRole('button', { name: /^save/i }).click();
  await expect(page).toHaveURL(new RegExp(`/recipes/${recipeId}$`));
  await expect(page.getByRole('heading', { name: saved.title })).toBeVisible();
  expect(attempts).toBe(2);
});

for (const eligible of [false, true]) {
  test(`capability separates format support from planning eligibility ${eligible}`, async ({ page }, testInfo) => {
    await page.route('**/api/integrations/status', route => route.fulfill({ json: {
      ai: { configured: true, reachable: true, provider: 'fixture', model: 'test-model', statsEnabled: true,
        modelTestState: 'completed', modelTestVerdict: eligible ? 'passed' : 'failed' },
      freezy: { configured: false, reachable: false }, scraper: { configured: false, reachable: false },
      webSearch: { configured: false, reachable: false, operational: false }
    } }));
    await page.route('**/api/integrations/ai/test', route => route.fulfill({ json: {
      state: 'completed', result: {
        testedAt: '2026-11-12T12:00:00Z', verdict: eligible ? 'passed' : 'failed', responseMode: 'JsonSchema',
        endpointReachable: true, modelListed: true, formatCompatible: true, evaluationPassed: eligible,
        planningQualityPassed: true, planningEligible: eligible, tokensPerSecond: 40, elapsedMs: 500,
        checks: [
          { name: 'Structured JSON reply', passed: true, detail: 'Native schema probe succeeded' },
          { name: 'All days filled', passed: eligible, detail: 'Planning example' }
        ]
      }
    } satisfies AiModelTestStatus }));
    await page.goto('/settings');
    await expect(page.getByText(eligible ? 'AI planning enabled: required checks passed'
      : 'AI planning disabled: required checks have not passed', { exact: true })).toBeVisible();
    await expect(page.getByText('The model returns meal plans in a format Dishhive can use.', { exact: true })).toBeVisible();
    await expect(page.getByText(/Other AI tasks have not been quality-tested/)).toBeVisible();
    for (const width of [360, 1280]) {
      await page.setViewportSize({ width, height: 900 });
      await page.locator('.ai-test').scrollIntoViewIfNeeded();
      await assertGeometry(page);
      await page.locator('.ai-test').screenshot({ path: testInfo.outputPath(`capability-${eligible}-${width}.png`) });
    }
  });
}
