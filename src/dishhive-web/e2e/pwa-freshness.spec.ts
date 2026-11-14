import { expect, test } from '@playwright/test';

test('production PWA exposes saved-data age, refreshes mutations and images, rejects offline writes and adopts an app update', async ({ page, context, request }, testInfo) => {
  const recipePath = '/recipes/11111111-1111-1111-1111-111111111111';
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.addInitScript(() => localStorage.setItem('dishhive.onboarding.resolved.v1', 'true'));
  await page.goto(recipePath);
  await expect(page.getByRole('heading', { name: 'PWA fixture soup', exact: true })).toBeVisible();
  await page.evaluate(async () => { await navigator.serviceWorker.ready; });
  await page.reload();
  await expect.poll(() => page.evaluate(() => !!navigator.serviceWorker.controller)).toBe(true);
  await page.goto('/shopping-list');
  await expect(page.locator('.item-name')).toHaveText('carrots');

  await page.goto(recipePath);
  await page.getByRole('button', { name: 'Plan now!' }).click();
  await page.locator('.date-option').first().click();
  await expect.poll(async () => (await (await request.get('/__fixture')).json()).writes).toBe(1);
  await page.goto('/shopping-list');
  await expect(page.locator('.item-name')).toHaveText('beans');
  await expect(page.locator('.saved-data-notice')).toHaveCount(0);

  await request.post('/__fixture', { data: { imageVersion: 2 } });
  await page.goto(recipePath);
  await expect(page.locator('.recipe-image')).toHaveAttribute('src', /image\?v=2$/);
  await expect.poll(async () => (await (await request.get('/__fixture')).json()).images).toContain('2');
  // Warm the actual quick-plan reads, then close without changing the plan.
  await page.getByRole('button', { name: 'Plan now!' }).click();
  await page.getByRole('button', { name: 'Cancel', exact: true }).click();
  await page.goto('/shopping-list');
  await expect(page.locator('.item-name')).toHaveText('beans');

  await request.post('/__fixture', { data: { outage: true } });
  await page.reload();
  await expect(page.locator('.item-name')).toHaveText('beans');
  await expect(page.locator('.saved-data-notice')).toContainText('Showing saved data from');
  expect(await page.evaluate(() => navigator.onLine)).toBe(true);
  await page.screenshot({ path: testInfo.outputPath('api-outage-360.png') });
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.screenshot({ path: testInfo.outputPath('api-outage-1280.png') });
  await page.emulateMedia({ colorScheme: 'dark' });
  await page.screenshot({ path: testInfo.outputPath('api-outage-dark-1280.png') });
  await page.setViewportSize({ width: 360, height: 900 });
  await page.screenshot({ path: testInfo.outputPath('api-outage-dark-360.png') });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);

  await context.setOffline(true);
  await page.reload();
  await expect(page.locator('.item-name')).toHaveText('beans');
  await expect(page.locator('.saved-data-notice')).toBeVisible();
  await page.goto(recipePath);
  await page.getByRole('button', { name: 'Plan now!' }).click();
  await page.locator('.date-option').first().click();
  await expect(page.getByText('Could not plan the recipe', { exact: true })).toBeVisible();

  await context.setOffline(false);
  await request.post('/__fixture', { data: { outage: false, item: 'peppers' } });
  await page.goto('/shopping-list');
  await expect(page.locator('.item-name')).toHaveText('peppers');
  await expect(page.locator('.saved-data-notice')).toHaveCount(0);
  expect((await (await request.get('/__fixture')).json()).writes).toBe(1);

  await request.post('/__fixture', { data: { version: 2 } });
  await page.evaluate(() => navigator.serviceWorker.controller!.postMessage({ action: 'CHECK_FOR_UPDATES', nonce: 101 }));
  await expect(page.getByText('A new version of Dishhive is available', { exact: true })).toBeVisible({ timeout: 30_000 });
  await page.getByRole('button', { name: 'Update', exact: true }).click();
  await expect(page.locator('html')).toHaveAttribute('data-pwa-version', '2');
  await expect(page.locator('.item-name')).toHaveText('peppers');
  expect(errors).toEqual([]);
});
