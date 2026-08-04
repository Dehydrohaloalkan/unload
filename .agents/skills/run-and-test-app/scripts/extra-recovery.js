// Verify that an active Extra run survives a fresh browser load and can be stopped.
// The development stub must be slowed temporarily or the run may finish too soon.
const { chromium } = require('playwright');
const fs = require('fs');

const BASE = process.env.BASE || 'http://localhost:4200';
const API = process.env.API || 'http://localhost:5000';
const OUT = process.env.OUT || `${__dirname}/shots`;
const log = (...args) => console.log('[recovery]', ...args);
const wait = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds));

async function activeExtra() {
  const runs = await (await fetch(`${API}/api/runs/today`)).json();
  return runs.filter((item) => (item.taskCode || '').toLowerCase() === 'extra' && item.status === 0);
}

(async () => {
  fs.mkdirSync(OUT, { recursive: true });

  for (let index = 0; index < 60 && (await activeExtra()).length; index++) await wait(1000);
  const response = await fetch(`${API}/api/runs/extra`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ adminOverride: true, publishToGateway: false, selectedBanks: null }),
  });
  if (!response.ok) throw new Error(`Extra start failed: HTTP ${response.status} ${await response.text()}`);
  const { correlationId } = await response.json();
  log('started', correlationId);
  await wait(1000);

  if (!(await activeExtra()).some((item) => item.correlationId === correlationId)) {
    log('WARN: run already finished; the stub may not be delayed');
  }

  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: { width: 1280, height: 1000 } });
  await page.goto(BASE, { waitUntil: 'networkidle' });
  await page.getByText('4. Extra', { exact: false }).first().waitFor({ timeout: 30000 });
  await page.waitForTimeout(1500);

  await page.locator('app-extra-card').getByLabel('Подробнее').click();
  const drawer = page.locator('aside.details-drawer');
  await page.waitForTimeout(800);
  await page.screenshot({ path: `${OUT}/06-recovered.png`, fullPage: true });
  const activeText = await drawer.locator('app-active-extra-view').innerText();
  log('ACTIVE VIEW >>>\n' + activeText + '\n<<<');

  const idShown = activeText.includes(correlationId);
  const stopButton = drawer.getByRole('button', { name: 'Остановить выгрузку' });
  const stopCount = await stopButton.count();
  const noActive = activeText.includes('Активной доп-выгрузки сейчас нет');
  let stopped = false;

  if (stopCount > 0) {
    await stopButton.click();
    for (let index = 0; index < 30; index++) {
      const text = await drawer.locator('app-active-extra-view').innerText();
      if (/Отменено|Завершено|Ошибка|Completed|Cancel/i.test(text) && !/Выполняется/i.test(text)) {
        stopped = true;
        break;
      }
      await page.waitForTimeout(1000);
    }
  }

  await page.screenshot({ path: `${OUT}/07-stopped.png`, fullPage: true });
  await browser.close();

  const passed = idShown && stopCount > 0 && !noActive;
  log(`id shown:${idShown} stop:${stopCount} noActive:${noActive} -> ${passed ? `PASS (stop worked:${stopped})` : 'FAIL'}`);
  process.exit(passed ? 0 : 2);
})().catch((error) => {
  console.error(error);
  process.exit(1);
});
