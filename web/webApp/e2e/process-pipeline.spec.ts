import { expect, test } from '@playwright/test';

test('keeps the process pipeline compact, bounded, accessible and fullscreen-capable', async ({
  page,
}) => {
  const snapshot = processSnapshot();
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.route('**/api/runs/active', (route) =>
    route.fulfill({ contentType: 'application/json', body: JSON.stringify(snapshot) }),
  );
  await page.route('**/api/runs/req-process-e2e', (route) =>
    route.fulfill({ contentType: 'application/json', body: JSON.stringify(snapshot) }),
  );

  await page.goto('/');
  await expect(page.locator('.stage-stack')).toBeVisible();
  await page.locator('app-run-card button[aria-label="Подробнее"]').click();
  await page.getByRole('tab', { name: 'Процесс' }).click();

  const pipeline = page.getByTestId('process-pipeline');
  await expect(pipeline).toBeVisible();
  await expect(page.locator('.process-handler')).toHaveCount(2);
  await expect(page.locator('.process-zone')).toHaveCount(5);
  await expect(page.locator('[data-testid^="process-worker-"]')).toHaveCount(4);
  await expect(page.getByTestId('process-worker-2')).toContainText('SCRIPT_RUNNING');
  await expect(pipeline).not.toContainText('→');

  const cardSelectors = [
    '[data-testid="process-zone-members"] .process-card',
    '[data-testid="process-node-resolver"] .process-card',
    '[data-testid="process-zone-scripts"] .process-card',
    '[data-testid="process-worker-2"] .process-card',
    '.process-file-group__card',
    '[data-testid="process-node-sender"] .process-card',
  ];
  await checkCardSizes(page, cardSelectors);
  await checkProcessBounds(page, 2);

  const fileGroup = page.locator('[data-testid^="process-file-group-"]').first();
  // The UI deliberately localizes large counts with a non-breaking space.
  await expect(fileGroup).toContainText(/Файлов:\s*1\s000/);
  await expect(fileGroup.getByRole('button', { name: 'Показать причину ошибки' })).toBeVisible();
  await expect(fileGroup.locator('.process-file-card')).toHaveCount(0);
  await fileGroup.getByRole('button', { name: /Показать файлы/ }).click();
  await expect(fileGroup.locator('.process-file-card')).toHaveCount(20);
  await expect(fileGroup).toContainText('1–20 из 1000');
  await fileGroup.getByRole('button', { name: 'Следующие 20' }).click();
  await expect(fileGroup.locator('.process-file-card')).toHaveCount(20);
  await expect(fileGroup).toContainText('21–40 из 1000');

  const sender = page.getByTestId('process-node-sender');
  await sender.locator('.process-card').first().click();
  await expect(sender.locator('.process-dispatch-file')).toHaveCount(20);

  const delivered = page.getByTestId('process-zone-delivered');
  await delivered.getByRole('button', { name: 'Показать отправленные' }).click();
  cardSelectors.push('[data-testid="process-zone-delivered"] .process-card');
  await checkCardSizes(page, cardSelectors);
  await delivered.locator('.process-delivery-group__summary').first().click();
  await expect(delivered.locator('.process-dispatch-file')).toHaveCount(20);
  await expect(page.locator('.process-file-card, .process-dispatch-file')).toHaveCount(60);
  await checkProcessBounds(page, 2);

  await fileGroup.getByRole('button', { name: 'Показать причину ошибки' }).click();
  const details = page.getByTestId('process-error-details');
  await expect(details.getByRole('dialog')).toBeVisible();
  await expect(details).toContainText('disk-full');
  await expect(details).toContainText('Недостаточно места для файла');
  await expect(page.getByTestId('process-error-close')).toBeFocused();
  await page.keyboard.press('Shift+Tab');
  await expect(details.getByRole('button', { name: 'Закрыть' }).last()).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(page.getByTestId('process-error-close')).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(details).toBeHidden();

  const fullscreen = page.getByTestId('process-fullscreen');
  await fullscreen.click();
  await expect
    .poll(() => page.evaluate(() => document.fullscreenElement?.getAttribute('data-testid')))
    .toBe('process-root');
  await expect(fullscreen).toHaveAttribute('aria-pressed', 'true');
  await fullscreen.click();
  await expect.poll(() => page.evaluate(() => document.fullscreenElement)).toBeNull();

  await page.setViewportSize({ width: 1400, height: 900 });
  await checkProcessBounds(page, 2);
  await checkCardSizes(page, cardSelectors);

  await page.setViewportSize({ width: 375, height: 900 });
  await checkProcessBounds(page, 1);
  await checkCardSizes(page, cardSelectors);
});

async function checkCardSizes(page: import('@playwright/test').Page, selectors: string[]) {
  const groups = await Promise.all(
    selectors.map(async (selector) => {
      const cards = page.locator(`${selector}:visible`);
      await expect(cards.first(), `visible card for ${selector}`).toBeVisible();
      expect(await cards.count(), `visible cards for ${selector}`).toBeGreaterThan(0);
      const boxes = await cards.evaluateAll((elements) =>
        elements.map((element) => {
          const bounds = element.getBoundingClientRect();
          return { width: bounds.width, height: bounds.height };
        }),
      );
      return boxes.map((box) => ({ selector, ...box }));
    }),
  );
  const boxes = groups.flat();
  const reference = boxes.find((box) => box.selector === '.process-file-group__card')!;
  for (const box of boxes) {
    expect(Math.abs(box.width - reference.width), `width of ${box.selector}`).toBeLessThanOrEqual(
      1,
    );
    expect(
      Math.abs(box.height - reference.height),
      `height of ${box.selector}`,
    ).toBeLessThanOrEqual(1);
  }
  expect(reference.width).toBe(256);
  expect(reference.height).toBe(192);
}

async function checkProcessBounds(page: import('@playwright/test').Page, workerColumns: number) {
  const layout = await page.evaluate(() => {
    const root = document.querySelector<HTMLElement>('[data-testid="process-root"]')!;
    const workerGrid = root.querySelector<HTMLElement>('.process-worker-grid')!;
    const cards = Array.from(
      root.querySelectorAll<HTMLElement>('.process-card, .process-file-group__card'),
    );
    return {
      documentOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
      rootOverflow: root.scrollWidth - root.clientWidth,
      workerColumns: getComputedStyle(workerGrid).gridTemplateColumns.split(' ').length,
      overflowingCards: cards
        .filter(
          (card) =>
            card.scrollWidth > card.clientWidth + 1 || card.scrollHeight > card.clientHeight + 1,
        )
        .map((card) => card.outerHTML.slice(0, 160)),
    };
  });
  expect(layout.documentOverflow).toBeLessThanOrEqual(1);
  expect(layout.rootOverflow).toBeLessThanOrEqual(1);
  expect(layout.workerColumns).toBe(workerColumns);
  expect(layout.overflowingCards).toEqual([]);
}

function processSnapshot() {
  const timestamp = '2026-09-23T10:01:00Z';
  const files = Object.fromEntries(
    Array.from({ length: 1_000 }, (_, index) => {
      const number = index + 1;
      const failed = number === 1_000;
      return [
        `file-${number}`,
        {
          id: `file-${number}`,
          parentScriptId: 'script-created',
          memberName: 'БанкФайлыОченьДлинноеНазваниеБезПробеловДляПроверкиКарточки',
          scriptCode: 'SCRIPT_CREATED',
          chunkNumber: number,
          sequence: 100 + number,
          stage: failed ? 2 : 1,
          createdAt: timestamp,
          queuedAt: timestamp,
          stageEnteredAt: timestamp,
          updatedAt: timestamp,
          completedAt: timestamp,
          workerId: 2,
          rows: 10,
          estimatedBytes: 1024,
          fileName: `created-${number}.txt`,
          filePath: `/created/${number}.txt`,
          failure: failed
            ? {
                entityType: 'file',
                entityId: `file-${number}`,
                stage: 'file_write',
                code: 'disk-full',
                message: 'Недостаточно места для файла',
                occurredAt: timestamp,
                memberName: 'Банк Файлы',
                scriptCode: 'SCRIPT_CREATED',
                workerId: 2,
                filePath: `/created/${number}.txt`,
                chunkNumber: number,
                batchId: null,
              }
            : null,
        },
      ];
    }),
  );
  const planned = (prefix: string, sent: boolean) =>
    Array.from({ length: 1_000 }, (_, index) => ({
      fileName: `${prefix}-${index + 1}.txt`,
      filePath: `/${prefix}/${index + 1}.txt`,
      queuedAt: timestamp,
      sentAt: sent ? timestamp : null,
      estimatedBytes: 1024,
      actualBytes: sent ? 1000 : null,
    }));
  const sentFiles = Array.from({ length: 1_000 }, (_, index) => ({
    filePath: `/delivered/${index + 1}.txt`,
    sentAt: timestamp,
  }));

  return {
    correlationId: 'req-process-e2e',
    taskCode: 'run',
    status: 0,
    targetCodes: ['INPUT', 'RESOLVER', 'QUEUE', 'WORKER'],
    createdAt: '2026-09-23T10:00:00Z',
    updatedAt: timestamp,
    memberStatuses: {
      input: {
        memberName: 'Банк Вход',
        status: 0,
        queuePosition: 1,
        sequence: 1,
        updatedAt: timestamp,
      },
      resolver: {
        memberName: 'Банк Поиск',
        status: 1,
        queuePosition: 2,
        sequence: 2,
        updatedAt: timestamp,
      },
    },
    scriptStatuses: {
      queued: {
        id: 'script-queued',
        memberName: 'Банк Очередь',
        scriptCode: 'SCRIPT_QUEUED',
        stage: 0,
        workOrder: 1,
        sequence: 10,
        discoveredAt: timestamp,
        stageEnteredAt: timestamp,
        updatedAt: timestamp,
      },
      running: {
        id: 'script-running',
        memberName: 'Банк Рабочий',
        scriptCode: 'SCRIPT_RUNNING',
        stage: 1,
        workOrder: 2,
        sequence: 11,
        workerId: 2,
        discoveredAt: timestamp,
        stageEnteredAt: timestamp,
        startedAt: timestamp,
        updatedAt: timestamp,
      },
    },
    workerStatuses: {
      two: {
        workerId: 2,
        state: 'Running',
        memberName: 'Банк Рабочий',
        scriptCode: 'SCRIPT_RUNNING',
        sequence: 12,
        updatedAt: timestamp,
      },
    },
    fileStatuses: files,
    senderBatches: {
      sending: {
        batchId: 'batch-sending',
        memberName: 'Банк Отправка',
        status: 1,
        sequence: 500,
        queuedAt: timestamp,
        startedAt: timestamp,
        updatedAt: timestamp,
        fileCount: 1_000,
        sentFiles: [],
        plannedFiles: planned('sender', false),
      },
      delivered: {
        batchId: 'batch-delivered',
        memberName: 'БанкГотовОченьДлинноеНазваниеБезПробеловДляПроверкиКарточки',
        status: 2,
        sequence: 501,
        queuedAt: timestamp,
        startedAt: timestamp,
        updatedAt: timestamp,
        fileCount: 1_000,
        sentFiles,
        plannedFiles: planned('delivered', true),
      },
    },
  };
}
