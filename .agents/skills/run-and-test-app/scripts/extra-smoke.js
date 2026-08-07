// Smoke test for the Extra UI and history bank names.
// Run from a scratch directory containing Playwright.
const { chromium } = require('playwright');
const fs = require('fs');

const BASE = process.env.BASE || 'http://localhost:4200';
const OUT = process.env.OUT || `${__dirname}/shots`;
const pad2 = (value) => String(value).padStart(2, '0');
const log = (...args) => console.log('[smoke]', ...args);

async function shot(page, name) {
  await page.screenshot({ path: `${OUT}/${name}.png`, fullPage: true });
  log('screenshot:', name);
}

async function enableAdmin(page) {
  const now = new Date();
  const password = `${pad2(now.getHours())}${pad2(now.getMinutes())}`;
  const button = page.getByRole('button', { name: /Админ-режим/i }).first();
  if (!/ВКЛ/i.test(await button.innerText())) {
    await button.click();
    await page.locator('#admin-password').fill(password);
    await page.getByRole('button', { name: 'Войти' }).click();
    await page.waitForTimeout(400);
  }
}

(async () => {
  fs.mkdirSync(OUT, { recursive: true });
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: { width: 1280, height: 1400 } });
  page.on('console', (message) => {
    if (message.type() === 'error') log('PAGE-ERR', message.text());
  });

  await page.goto(BASE, { waitUntil: 'networkidle' });
  await page.getByText('4. Extra', { exact: false }).first().waitFor({ timeout: 30000 });
  await shot(page, '01-dashboard');

  await enableAdmin(page);
  await page.locator('app-extra-card').getByLabel('Подробнее').click();
  const drawer = page.locator('aside.details-drawer');
  await drawer.locator('.bank-item').first().waitFor({ timeout: 15000 });
  log('bank items:', await drawer.locator('.bank-item').count());
  await shot(page, '02-extra-panel');

  await drawer.locator('.bank-item').nth(0).locator('mat-checkbox').click();
  await drawer.locator('.bank-item').nth(1).locator('mat-checkbox').click();
  await page.waitForTimeout(300);
  await shot(page, '03-indeterminate');
  await drawer.locator('.details-check-row', { hasText: 'Выбрать все' }).locator('mat-checkbox').click();

  await drawer.locator('.details-check-row', { hasText: 'Отправлять в шлюз' }).locator('mat-checkbox').click();
  const idsBefore = await page.evaluate(async () => (await (await fetch('/api/runs/today')).json())
    .filter((item) => (item.taskCode || '').toLowerCase() === 'extra')
    .map((item) => item.correlationId));
  await drawer.getByRole('button', { name: 'Запустить extra' }).click();

  try {
    const dialog = page.getByRole('dialog');
    const accept = dialog.getByRole('button', { name: 'Запустить', exact: true });
    await accept.waitFor({ timeout: 3000 });
    await accept.click();
    await dialog.waitFor({ state: 'detached', timeout: 5000 });
  } catch {}

  for (let index = 0; index < 80; index++) {
    const runs = await page.evaluate(async () => (await (await fetch('/api/runs/today')).json())
      .filter((item) => (item.taskCode || '').toLowerCase() === 'extra'));
    const fresh = runs.filter((item) => !idsBefore.includes(item.correlationId));
    if (fresh.length && fresh.every((item) => item.status !== 0 && item.status !== 4)) break;
    await page.waitForTimeout(500);
  }
  await page.waitForTimeout(800);
  await shot(page, '04-after-run');

  await drawer.getByText('История', { exact: true }).click();
  await page.waitForTimeout(500);
  await drawer.locator('.history-run__summary').first().click();
  await drawer.locator('.history-script .history-member__summary').first().click();
  await drawer.locator('.history-bank .history-member__summary').first().click();
  await page.waitForTimeout(300);
  await shot(page, '05-history');
  log('HISTORY TEXT >>>\n' + await drawer.locator('app-run-history-list').innerText() + '\n<<<');

  await drawer.getByRole('button', { name: 'Закрыть панель' }).click();
  await page.setViewportSize({ width: 375, height: 812 });
  const adminButton = page.getByRole('button', { name: /Админ-режим/i }).first();
  await adminButton.click();
  await adminButton.click();
  const adminDialog = page.getByRole('dialog');
  await adminDialog.waitFor();
  await page.waitForTimeout(250);
  const focusedField = await page.evaluate(() => document.activeElement?.id);
  if (focusedField !== 'admin-password') throw new Error(`Unexpected dialog focus: ${focusedField}`);
  await shot(page, '06-mobile-admin-dialog');
  await page.keyboard.press('Escape');
  await adminDialog.waitFor({ state: 'detached' });
  if (!(await adminButton.evaluate((element) => element === document.activeElement))) {
    throw new Error('Admin button focus was not restored after closing the dialog');
  }

  await page.locator('app-extra-card').getByLabel('Подробнее').click();
  await page.locator('aside.details-drawer .bank-item').first().waitFor();
  await page.waitForTimeout(250);
  const hasHorizontalOverflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
  if (hasHorizontalOverflow) throw new Error('Mobile layout has horizontal overflow');
  await shot(page, '07-mobile-extra-panel');

  await browser.close();
  log('done');
})().catch((error) => {
  console.error(error);
  process.exit(1);
});
