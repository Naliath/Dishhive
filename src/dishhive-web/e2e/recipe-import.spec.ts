import { expect, Page, test } from '@playwright/test';

async function fixtures(page: Page) {
  await page.addInitScript(() => localStorage.setItem('dishhive.onboarding.resolved.v1', 'true'));
  await page.route('**/api/**', async route => {
    const path = new URL(route.request().url()).pathname;
    let json: unknown = [];
    if (path === '/api/recipes/import/capabilities' || path === '/api/recipes/import/image-capability')
      json = { textAvailable: true, photoState: 'available', maxTextCharacters: 30000, maxImageBytes: 15728640 };
    else if (path === '/api/settings/preferences') json = { preferredLanguage: 'en', translateImportedRecipes: false, householdTimeZone: 'Europe/Brussels', supportedLanguages: [{ code: 'en', displayName: 'English' }] };
    else if (path === '/api/recipes/page') json = { items: [], totalCount: 0, page: 1, pageSize: 24 };
    else if (path === '/api/statistics/dishes') json = { dishes: [] };
    else if (path === '/api/recipes/import/text' || path === '/api/recipes/import/image') {
      await route.fulfill({ status: 422, json: { title: 'Could not extract a complete recipe.', detail: 'Include readable ingredients and preparation steps.' } }); return;
    }
    await route.fulfill({ json });
  });
}

async function geometry(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(await page.evaluate(() => innerWidth));
  const form = page.locator('app-recipe-import form');
  expect(await form.evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
}

for (const width of [360, 1280]) {
  test(`recipe sources, retained errors and defaults at ${width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 }); await fixtures(page);
    await page.goto('/recipes');
    await page.getByRole('button', { name: 'Import from URL', exact: true }).click();
    await expect(page.getByRole('textbox', { name: 'Recipe URL' })).toBeVisible();
    await geometry(page); await page.screenshot({ path: testInfo.outputPath(`url-${width}.png`), fullPage: true });
    await page.getByRole('combobox', { name: 'Import source' }).click();
    await page.getByRole('option', { name: 'Text', exact: true }).click();
    const text = page.getByRole('textbox', { name: 'Recipe text', exact: true });
    await text.fill('Pancakes\n200 g flour\n250 ml milk\nMix and cook.');
    await page.getByRole('button', { name: 'Import', exact: true }).click();
    await expect(page.getByRole('alert')).toContainText('Include readable ingredients');
    await expect(text).toHaveValue(/200 g flour/);
    await geometry(page); await page.screenshot({ path: testInfo.outputPath(`text-error-${width}.png`), fullPage: true });
    await page.reload();
    await page.getByRole('button', { name: 'Import from text', exact: true }).click();
    await expect(page.getByRole('combobox', { name: 'Import source' })).toContainText('Text');
    await page.getByRole('combobox', { name: 'Import source' }).click();
    await page.getByRole('option', { name: 'Photo', exact: true }).click();
    const sample = await page.context().newPage();
    await sample.setContent('<article style="width:240px;padding:16px;font:18px sans-serif;background:white;color:black"><h2>Pancakes</h2><p>200 g flour<br>250 ml milk</p><p>Mix the ingredients.<br>Cook in a hot pan.</p></article>');
    const photo = await sample.locator('article').screenshot(); await sample.close();
    await page.locator('input[type=file]').first().setInputFiles({ name: 'recipe.png', mimeType: 'image/png', buffer: photo });
    await expect(page.getByAltText('Recipe photo preview')).toBeVisible();
    await page.getByRole('button', { name: 'Import', exact: true }).click();
    await expect(page.getByRole('alert')).toBeVisible();
    await expect(page.getByAltText('Recipe photo preview')).toBeVisible();
    await geometry(page); await page.screenshot({ path: testInfo.outputPath(`photo-error-${width}.png`), fullPage: true });
    await page.reload(); await page.getByRole('button', { name: 'Import from photo', exact: true }).click();
    await expect(page.getByRole('combobox', { name: 'Import source' })).toContainText('Photo');
  });

  test(`remembered source survives unavailable AI at ${width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 }); await fixtures(page);
    await page.addInitScript(() => localStorage.setItem('dishhive.recipeImport.source.v1', 'photo'));
    await page.route('**/api/recipes/import/capabilities', route => route.fulfill({ json: { textAvailable: false, photoState: 'unavailable', maxTextCharacters: 30000, maxImageBytes: 15728640 } }));
    await page.goto('/recipes'); await page.getByRole('button', { name: 'Import from photo', exact: true }).click();
    await expect(page.getByRole('status')).toContainText('require a configured AI service');
    await expect(page.getByRole('combobox', { name: 'Import source' })).toContainText('Photo');
    await geometry(page); await page.screenshot({ path: testInfo.outputPath(`unavailable-${width}.png`), fullPage: true });
    await page.getByRole('combobox', { name: 'Import source' }).click();
    await page.getByRole('option', { name: 'URL', exact: true }).click();
    await page.getByRole('textbox', { name: 'Recipe URL' }).fill('https://example.com/recipe');
    await expect(page.getByRole('button', { name: 'Import', exact: true })).toBeEnabled();
  });
}

test('mobile photo capture uses the rear-camera file input', async ({ browser }) => {
  const context = await browser.newContext({ viewport: { width: 360, height: 900 }, isMobile: true, hasTouch: true });
  const page = await context.newPage(); await fixtures(page);
  // Desktop Chrome's mobile emulation has no native media-capture property. Supply
  // that platform capability here; physical camera capture is a separate device check.
  await page.addInitScript(() => Object.defineProperty(HTMLInputElement.prototype, 'capture', { configurable: true, value: '' }));
  await page.goto('http://127.0.0.1:4300/recipes');
  await page.getByRole('button', { name: 'Import from URL', exact: true }).click();
  await page.getByRole('combobox', { name: 'Import source' }).click();
  await page.getByRole('option', { name: 'Photo', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Take picture', exact: true })).toBeVisible();
  await expect(page.locator('input[capture]')).toHaveAttribute('capture', 'environment');
  await context.close();
});

test('successful text import opens the saved recipe', async ({ page }) => {
  await fixtures(page);
  const id = '11111111-1111-1111-1111-111111111111';
  const recipe = { id, title: 'Pancakes', servings: 2, hasLocalImage: false, tags: [], cookbookIds: [],
    ingredients: [{ id, name: 'flour', quantity: 200, unit: 'g', originalText: '200 g flour', sortOrder: 0 }],
    steps: [{ id, stepNumber: 1, instruction: 'Mix and cook.' }],
    dietaryFacts: { contains: [], status: 0, recipeRevision: 1, isCurrent: false, history: [] },
    createdAt: '2026-11-15T12:00:00Z', updatedAt: '2026-11-15T12:00:00Z' };
  await page.route('**/api/recipes/import/text', route => route.fulfill({ status: 201, json: recipe }));
  await page.route(`**/api/recipes/${id}`, route => route.fulfill({ json: recipe }));
  await page.goto('/recipes'); await page.getByRole('button', { name: 'Import from URL', exact: true }).click();
  await page.getByRole('combobox', { name: 'Import source' }).click(); await page.getByRole('option', { name: 'Text', exact: true }).click();
  await page.getByRole('textbox', { name: 'Recipe text', exact: true }).fill('200 g flour. Mix and cook.');
  await page.getByRole('button', { name: 'Import', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/recipes/${id}$`)); await expect(page.getByRole('heading', { name: 'Pancakes' })).toBeVisible();
});
