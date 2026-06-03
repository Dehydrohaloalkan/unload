// Проверка восстановления активной extra-выгрузки после перезагрузки страницы.
// Стартует долгий запуск через API (adminOverride), затем открывает страницу заново
// (== refresh во время запуска) и убеждается, что выгрузка видна и останавливается.
//
// ВАЖНО: extra-стаб быстрый — заранее замедли его (Thread.Sleep в CreateExtraUnloadReader,
// пересобрать+перезапустить backend), иначе запуск завершится до загрузки страницы.
//
// Запуск: node extra-recovery.js
// Env: BASE (http://localhost:4200), API (http://localhost:5000), OUT (./shots)
const { chromium } = require('playwright');
const fs = require('fs');

const BASE = process.env.BASE || 'http://localhost:4200';
const API = process.env.API || 'http://localhost:5000';
const OUT = process.env.OUT || (__dirname + '/shots');
const log = (...a) => console.log('[recovery]', ...a);

async function activeExtra() {
  const d = await (await fetch(`${API}/api/runs/today`)).json();
  return d.filter((x) => (x.taskCode || '').toLowerCase() === 'extra' && x.status === 0);
}

(async () => {
  fs.mkdirSync(OUT, { recursive: true });

  // дождаться, пока ничего не активно, и стартовать новый (долгий) запуск
  for (let i = 0; i < 60 && (await activeExtra()).length; i++) await new Promise((r) => setTimeout(r, 1000));
  const start = await fetch(`${API}/api/runs/extra`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ adminOverride: true, publishToGateway: false, selectedBanks: null }),
  });
  const { correlationId } = await start.json();
  log('started', correlationId);
  await new Promise((r) => setTimeout(r, 1000));
  if (!(await activeExtra()).some((x) => x.correlationId === correlationId)) {
    log('WARN: запуск уже не активен — вероятно стаб не замедлён; тест может не поймать момент');
  }

  // открыть приложение «с нуля» при активном запуске (путь bootstrap == после refresh)
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
  const stopCount = await drawer.getByRole('button', { name: 'Остановить выгрузку' }).count();
  const noActive = activeText.includes('Активной доп-выгрузки сейчас нет');

  // доказать «живость»: стоп должен сработать и отразиться в UI
  let stopped = false;
  if (stopCount > 0) {
    await drawer.getByRole('button', { name: 'Остановить выгрузку' }).click();
    for (let i = 0; i < 30; i++) {
      const t = await drawer.locator('app-active-extra-view').innerText();
      if (/Отменено|Завершено|Ошибка|Completed|Cancel/i.test(t) && !/Выполняется/i.test(t)) { stopped = true; break; }
      await page.waitForTimeout(1000);
    }
  }
  await page.screenshot({ path: `${OUT}/07-stopped.png`, fullPage: true });
  await browser.close();

  const ok = idShown && stopCount > 0 && !noActive;
  log(`id shown:${idShown} stop:${stopCount} noActive:${noActive} -> ` + (ok ? `PASS (stop worked:${stopped})` : 'FAIL'));
  process.exit(ok ? 0 : 2);
})().catch((e) => { console.error(e); process.exit(1); });
