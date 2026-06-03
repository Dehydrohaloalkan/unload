// Smoke test для UI экстра-выгрузки: вид панели (один список банков, select-all,
// чекбокс шлюза сверху), запуск выгрузки и проверка названий банков в истории.
//
// Запуск (из скретч-папки с установленным playwright):
//   node extra-smoke.js
// Env: BASE (по умолчанию http://localhost:4200), API (http://localhost:5000), OUT (./shots)
const { chromium } = require('playwright');
const fs = require('fs');

const BASE = process.env.BASE || 'http://localhost:4200';
const API = process.env.API || 'http://localhost:5000';
const OUT = process.env.OUT || (__dirname + '/shots');
const pad2 = (n) => String(n).padStart(2, '0');
const log = (...a) => console.log('[smoke]', ...a);

async function shot(page, name) {
  await page.screenshot({ path: `${OUT}/${name}.png`, fullPage: true });
  log('screenshot:', name);
}

async function enableAdmin(page) {
  const now = new Date();
  const pwd = `${pad2(now.getHours())}${pad2(now.getMinutes())}`;
  const btn = page.getByRole('button', { name: /Админ-режим/i }).first();
  if (!/ВКЛ/i.test(await btn.innerText())) {
    await btn.click();
    await page.locator('#admin-password').fill(pwd);
    await page.getByRole('button', { name: 'Войти' }).click();
    await page.waitForTimeout(400);
  }
}

(async () => {
  fs.mkdirSync(OUT, { recursive: true });
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: { width: 1280, height: 1400 } });
  page.on('console', (m) => { if (m.type() === 'error') log('PAGE-ERR', m.text()); });

  await page.goto(BASE, { waitUntil: 'networkidle' });
  await page.getByText('4. Extra', { exact: false }).first().waitFor({ timeout: 30000 });
  await shot(page, '01-dashboard');

  await enableAdmin(page);
  await page.locator('app-extra-card').getByLabel('Подробнее').click();
  const drawer = page.locator('aside.details-drawer');
  await drawer.locator('.bank-item').first().waitFor({ timeout: 15000 });
  log('bank items:', await drawer.locator('.bank-item').count());
  await shot(page, '02-extra-panel');
  log('PANEL TEXT >>>\n' + (await drawer.locator('app-details-extra-panel').innerText()) + '\n<<<');

  // select-all -> indeterminate при снятии части банков
  await drawer.locator('.bank-item').nth(0).locator('.p-checkbox').click();
  await drawer.locator('.bank-item').nth(1).locator('.p-checkbox').click();
  await page.waitForTimeout(300);
  await shot(page, '03-indeterminate');
  await drawer.locator('label.details-check-row', { hasText: 'Выбрать все' }).locator('.p-checkbox').click();

  // запуск с выключенным шлюзом (без FTP)
  await drawer.locator('label.details-check-row', { hasText: 'Отправлять в шлюз' }).locator('.p-checkbox').click();
  const idsBefore = await page.evaluate(async () => (await (await fetch('/api/runs/today')).json())
    .filter((x) => (x.taskCode || '').toLowerCase() === 'extra').map((x) => x.correlationId));
  await drawer.getByRole('button', { name: 'Запустить extra' }).click();

  // подтверждение, если extra уже была сегодня (exact, чтобы не задеть «Запустить extra»)
  try {
    const accept = page.locator('p-confirmdialog').getByRole('button', { name: 'Запустить', exact: true });
    await accept.waitFor({ timeout: 3000 });
    await accept.click();
    await page.locator('.p-dialog-mask').waitFor({ state: 'detached', timeout: 5000 });
  } catch {}

  // ждём завершения НОВОГО запуска (относительный путь — через прокси Angular, без CORS)
  for (let i = 0; i < 80; i++) {
    const runs = await page.evaluate(async () => (await (await fetch('/api/runs/today')).json())
      .filter((x) => (x.taskCode || '').toLowerCase() === 'extra'));
    const fresh = runs.filter((x) => !idsBefore.includes(x.correlationId));
    if (fresh.length && fresh.every((x) => x.status !== 0 && x.status !== 4)) break;
    await page.waitForTimeout(500);
  }
  await page.waitForTimeout(800);
  await shot(page, '04-after-run');

  // история: развернуть запуск -> скрипт -> банк, проверить названия банков
  await drawer.getByText('История', { exact: true }).click();
  await page.waitForTimeout(500);
  await drawer.locator('.history-run__summary').first().click();
  await page.waitForTimeout(300);
  await drawer.locator('.history-script .history-member__summary').first().click();
  await page.waitForTimeout(300);
  await drawer.locator('.history-bank .history-member__summary').first().click();
  await page.waitForTimeout(300);
  await shot(page, '05-history');
  log('HISTORY TEXT >>>\n' + (await drawer.locator('app-run-history-list').innerText()) + '\n<<<');

  await browser.close();
  log('done');
})().catch((e) => { console.error(e); process.exit(1); });
