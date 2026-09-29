import { expect, test } from '@playwright/test';

test('requires and submits a database password before loading the workflow', async ({ page }) => {
  await page.route('**/api/database/status', (route) =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        configured: false,
        selectedDatabaseId: null,
        databases: [
          { id: 'main', name: 'Основная' },
          { id: 'archive', name: 'Архив' },
        ],
      }),
    }),
  );
  await page.route('**/api/database/connect', async (route) => {
    expect(route.request().postDataJSON()).toEqual({
      databaseId: 'archive',
      username: 'playwright-user',
      password: 'playwright-secret',
    });
    await route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        configured: true,
        selectedDatabaseId: 'archive',
        databases: [
          { id: 'main', name: 'Основная' },
          { id: 'archive', name: 'Архив' },
        ],
      }),
    });
  });

  await page.goto('/');
  const dialog = page.getByRole('dialog', { name: 'Подключение к базе данных' });
  await expect(dialog).toBeVisible();
  await dialog.getByLabel('База данных').click();
  await page.getByRole('option', { name: 'Архив' }).click();
  await dialog.getByLabel('Пользователь').fill('playwright-user');
  await dialog.getByLabel('Пароль базы данных').fill('playwright-secret');
  await dialog.getByRole('button', { name: 'Подключиться' }).click();

  await expect(dialog).toBeHidden();
  await expect(page.locator('.stage-stack')).toBeVisible();
});
