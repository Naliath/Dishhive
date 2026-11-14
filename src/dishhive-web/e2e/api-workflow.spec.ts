import { expect, test } from '@playwright/test';
import type { FamilyMember } from '../src/app/models/family-member.model';
import type { Recipe } from '../src/app/models/recipe.model';
import type { PlannedMeal } from '../src/app/models/planned-meal.model';
import type { MealSuggestions } from '../src/app/models/meal-suggestion.model';
import type { ShoppingList } from '../src/app/models/shopping-list.model';

// Runs only against the disposable Kestrel/PostgreSQL host owned by LocalWorkflowTests.
// Browser API calls are never intercepted or fulfilled by this test.
test('import, edit, quick plan, generate and accept through the real local API', async ({ page, request }, testInfo) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('response', response => {
    if (new URL(response.url()).pathname.startsWith('/api/') && response.status() >= 400)
      errors.push(`${response.status()} ${response.url()}`);
  });
  const householdResponse = await request.post('/api/familymembers', { data: { name: 'Workflow household' } });
  expect(householdResponse.status()).toBe(201);
  const household = await householdResponse.json() as FamilyMember;
  const guestResponse = await request.post('/api/familymembers', { data: { name: 'Workflow guest', isGuest: true } });
  expect(guestResponse.status()).toBe(201);
  const inactiveResponse = await request.post('/api/familymembers', { data: { name: 'Inactive household' } });
  const inactive = await inactiveResponse.json() as FamilyMember;
  expect((await request.put(`/api/familymembers/${inactive.id}`, {
    data: { name: inactive.name, isActive: false }
  })).ok()).toBeTruthy();
  expect((await request.patch('/api/settings/preferences', { data: { preferredLanguage: 'en' } })).ok()).toBeTruthy();

  await page.goto('/recipes');
  await page.getByRole('button', { name: 'Import from URL', exact: true }).click();
  await page.getByRole('textbox', { name: 'Recipe URL' }).fill(
    'https://dagelijksekost.vrt.be/gerechten/cremeux-citroen-bodem-witte-chocolade-gepofte-rijst-rode-bessen');
  const importedResponse = page.waitForResponse(response =>
    new URL(response.url()).pathname === '/api/recipes/import' && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Import', exact: true }).click();
  const importedHttp = await importedResponse;
  expect(importedHttp.status()).toBe(201);
  const imported = await importedHttp.json() as Recipe;
  expect(imported.ingredients).toHaveLength(15);
  expect(imported.steps).toHaveLength(11);
  expect(imported.sourceProvider).toBe('dagelijkse-kost');

  await page.goto(`/recipes/${imported.id}/edit`);
  await page.getByRole('textbox', { name: /^title/i }).fill('Edited recipe from real API workflow');
  const editedResponse = page.waitForResponse(response =>
    new URL(response.url()).pathname === `/api/recipes/${imported.id}` && response.request().method() === 'PUT');
  await page.getByRole('button', { name: /^save/i }).click();
  expect((await editedResponse).ok()).toBeTruthy();
  await expect(page).toHaveURL(new RegExp(`/recipes/${imported.id}$`));
  await expect(page.getByRole('heading', { name: 'Edited recipe from real API workflow' })).toBeVisible();
  expect((await request.put(`/api/recipes/${imported.id}/facts`, {
    data: { contains: ['Milk', 'Eggs', 'Gluten'] }
  })).ok()).toBeTruthy();
  const edited = await (await request.get(`/api/recipes/${imported.id}`)).json() as Recipe;
  expect(edited.sourceUrl).toBe(imported.sourceUrl);
  expect(edited.ingredients).toHaveLength(imported.ingredients.length);

  await page.getByRole('button', { name: /plan now/i }).click();
  const quickDialog = page.getByRole('dialog');
  const quickResponse = page.waitForResponse(response =>
    new URL(response.url()).pathname === '/api/plannedmeals' && response.request().method() === 'POST');
  await quickDialog.getByRole('button', { name: /Today/ }).click();
  const quickHttp = await quickResponse;
  expect(quickHttp.status()).toBe(201);
  const quickMeal = await quickHttp.json() as PlannedMeal;
  expect(quickHttp.request().postDataJSON().familyMemberIds).toEqual([household.id]);
  expect(quickMeal.attendeeIds).toEqual([household.id]);
  expect(quickMeal.recipeId).toBe(imported.id);
  expect((await request.delete(`/api/plannedmeals/${quickMeal.id}`)).status()).toBe(204);

  await page.goto('/');
  await page.getByRole('button', { name: /suggest week/i }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('button', { name: /^generate/i })).toBeVisible();
  const dates = dialog.getByRole('group', { name: 'Choose the days to generate suggestions for.' }).getByRole('checkbox');
  await expect(dates).toHaveCount(7);
  for (const date of await dates.all()) await date.uncheck();
  await dates.last().check();
  const generatedResponse = page.waitForResponse(response =>
    new URL(response.url()).pathname === '/api/plannedmeals/suggestions' && response.request().method() === 'POST');
  await dialog.getByRole('button', { name: /^generate/i }).click();
  const generatedHttp = await generatedResponse;
  expect(generatedHttp.ok()).toBeTruthy();
  const generated = await generatedHttp.json() as MealSuggestions;
  expect(generated.suggestions).toHaveLength(1);
  expect(generated.suggestions[0].recipeId).toBe(imported.id);
  expect(generated.suggestions[0].attendeeIds).toEqual([household.id]);
  const acceptedResponse = page.waitForResponse(response =>
    new URL(response.url()).pathname === '/api/plannedmeals/suggestions/accept' && response.request().method() === 'POST');
  await dialog.getByRole('button', { name: 'Add 1 to plan', exact: true }).click();
  const acceptedHttp = await acceptedResponse;
  expect(acceptedHttp.ok()).toBeTruthy();
  const accepted = await acceptedHttp.json();
  expect(accepted.results[0].status).toBe('created');
  await expect(dialog).toBeHidden();
  await expect(page.getByText(edited.title, { exact: true })).toHaveCount(1);
  const retry = await request.post('/api/plannedmeals/suggestions/accept', { data: acceptedHttp.request().postDataJSON() });
  expect((await retry.json()).results[0].status).toBe('alreadyApplied');
  const date = generated.suggestions[0].date;
  const meals = await (await request.get(`/api/plannedmeals?from=${date}&to=${date}`)).json() as PlannedMeal[];
  expect(meals).toHaveLength(1);
  expect(meals[0].recipeId).toBe(imported.id);
  expect(meals[0].attendeeIds).toEqual([household.id]);
  const shopping = await (await request.get(`/api/shoppinglist?from=${date}&to=${date}`)).json() as ShoppingList;
  expect(shopping.items.length).toBeGreaterThan(0);
  expect(shopping.items.some(item => item.sourceRecipes.includes(edited.title))).toBeTruthy();
  expect(errors).toEqual([]);
  await page.screenshot({ path: testInfo.outputPath('workflow-complete.png'), fullPage: true });
});
