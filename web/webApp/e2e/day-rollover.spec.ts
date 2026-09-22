import { expect, test } from '@playwright/test';

test('refreshes the open dashboard after the server-local date changes', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-09-21T20:59:59.000Z') });

  let dashboardRequests = 0;
  await page.route('**/api/system/time', async (route) => {
    await route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        serverLocalTime: '2026-09-21T23:59:59+03:00',
        serverUtcTime: '2026-09-21T20:59:59Z',
        utcOffsetMinutes: 180,
        timeZoneId: 'Europe/Minsk',
      }),
    });
  });
  await page.route('**/api/runs/dashboard', async (route) => {
    dashboardRequests += 1;
    await route.continue();
  });

  await page.goto('/');
  await expect(page.locator('.stage-stack')).toBeVisible();
  await expect.poll(() => dashboardRequests).toBeGreaterThan(0);
  const requestsBeforeMidnight = dashboardRequests;

  await page.clock.fastForward(1_000);

  await expect.poll(() => dashboardRequests).toBeGreaterThan(requestsBeforeMidnight);
});
