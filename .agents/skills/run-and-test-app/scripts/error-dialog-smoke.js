// Verifies that a failed bootstrap request is presented as an accessible, prominent dialog.
// Run from a scratch directory containing Playwright.
const { chromium } = require('playwright');
const fs = require('fs');

const BASE = process.env.BASE || 'http://localhost:4200';
const OUT = process.env.OUT || `${__dirname}/shots`;
const ERROR_TEXT = 'Тестовая ошибка: каталог выгрузки недоступен.';

(async () => {
  fs.mkdirSync(OUT, { recursive: true });
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });

  await page.route('**/api/catalog', (route) =>
    route.fulfill({
      status: 500,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: 'Catalog unavailable', detail: ERROR_TEXT }),
    }),
  );
  await page.goto(BASE, { waitUntil: 'networkidle' });

  const dialog = page.getByRole('alertdialog');
  await dialog.waitFor({ timeout: 15000 });
  await dialog.getByText('Не удалось выполнить действие', { exact: true }).waitFor();
  await dialog.getByText(ERROR_TEXT, { exact: true }).waitFor();
  const closeButton = dialog.getByRole('button', { name: 'Понятно' });
  if (!(await closeButton.evaluate((element) => element === document.activeElement))) {
    throw new Error('Error dialog close button did not receive focus');
  }

  await page.screenshot({ path: `${OUT}/error-dialog-desktop.png`, fullPage: true });
  await page.setViewportSize({ width: 375, height: 812 });
  await page.screenshot({ path: `${OUT}/error-dialog-mobile.png`, fullPage: true });
  await closeButton.click();
  await dialog.waitFor({ state: 'detached' });

  await browser.close();
  console.log('[error-dialog] done');
})().catch((error) => {
  console.error(error);
  process.exit(1);
});
