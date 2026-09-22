import { expect, test } from '@playwright/test';

test('renders the vertical pipeline, bounded files and accessible failure details', async ({
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
  await expect(page.getByTestId('process-zone-members')).toBeAttached();
  await expect(page.getByTestId('process-node-resolver')).toBeAttached();
  await expect(page.getByTestId('process-zone-scripts')).toBeAttached();
  await expect(page.getByTestId('process-zone-workers')).toBeAttached();
  await expect(page.getByTestId('process-zone-files')).toBeAttached();
  await expect(page.getByTestId('process-node-sender')).toBeAttached();
  await expect(page.getByTestId('process-zone-delivered')).toBeAttached();
  await expect(page.locator('[data-testid^="process-worker-"]')).toHaveCount(4);
  await expect(page.getByTestId('process-worker-2')).toContainText('SCRIPT_RUNNING');

  const fileGroup = page.locator('[data-testid^="process-file-group-"]').first();
  await expect(fileGroup).toContainText('Файлов: 100');
  await expect(fileGroup.locator('.process-file-card')).toHaveCount(0);
  await fileGroup.getByRole('button', { name: /Показать файлы/ }).click();
  await expect(fileGroup.locator('.process-file-card')).toHaveCount(20);
  await fileGroup.getByRole('button', { name: 'Следующие 20' }).click();
  await expect(fileGroup.locator('.process-file-card')).toHaveCount(20);
  await expect(fileGroup).toContainText('21–40 из 100');
  await fileGroup.getByRole('button', { name: 'Предыдущие 20' }).click();
  await expect(fileGroup.locator('.process-file-card')).toHaveCount(20);
  await expect(fileGroup).toContainText('1–20 из 100');

  await fileGroup.getByRole('button', { name: 'Показать причину ошибки' }).click();
  const details = page.getByTestId('process-error-details');
  await expect(details.getByRole('dialog')).toBeVisible();
  await expect(details).toContainText('disk-full');
  await expect(details).toContainText('Недостаточно места для файла');
  await expect(page.getByTestId('process-error-close')).toBeFocused();
  await page.keyboard.press('Shift+Tab');
  await expect(details.locator('.process-dialog__close')).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(page.getByTestId('process-error-close')).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(details).toBeHidden();

  await page.setViewportSize({ width: 375, height: 900 });
  const horizontalOverflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
  expect(horizontalOverflow).toBeLessThanOrEqual(1);
  const workerColumns = await page
    .locator('.process-worker-grid')
    .evaluate((element) => getComputedStyle(element).gridTemplateColumns.split(' ').length);
  expect(workerColumns).toBe(1);
  const reducedTransitionSeconds = await fileGroup
    .getByRole('button', { name: 'Следующие 20' })
    .evaluate((element) => Number.parseFloat(getComputedStyle(element).transitionDuration));
  expect(reducedTransitionSeconds).toBeLessThan(0.001);
});

function processSnapshot() {
  const timestamp = '2026-09-22T10:01:00Z';
  const files = Object.fromEntries(
    Array.from({ length: 100 }, (_, index) => {
      const id = `file-${index + 1}`;
      const failed = index === 99;
      return [
        id,
        {
          id,
          parentScriptId: 'script-running',
          memberName: 'Банк Рабочий',
          scriptCode: 'SCRIPT_RUNNING',
          chunkNumber: index + 1,
          sequence: 100 + index,
          stage: failed ? 2 : 1,
          createdAt: timestamp,
          queuedAt: timestamp,
          stageEnteredAt: timestamp,
          updatedAt: timestamp,
          completedAt: timestamp,
          workerId: 2,
          rows: 10,
          estimatedBytes: 1024,
          fileName: `${id}.txt`,
          filePath: `/safe/${id}.txt`,
          failure: failed
            ? {
                entityType: 'file',
                entityId: id,
                stage: 'file_write',
                code: 'disk-full',
                message: 'Недостаточно места для файла',
                occurredAt: timestamp,
                memberName: 'Банк Рабочий',
                scriptCode: 'SCRIPT_RUNNING',
                workerId: 2,
                filePath: `/safe/${id}.txt`,
                chunkNumber: 100,
                batchId: null,
              }
            : null,
        },
      ];
    }),
  );

  return {
    correlationId: 'req-process-e2e',
    taskCode: 'run',
    status: 0,
    targetCodes: ['INPUT', 'RESOLVER', 'QUEUE', 'WORKER'],
    createdAt: '2026-09-22T10:00:00Z',
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
      worker: {
        memberName: 'Банк Рабочий',
        status: 1,
        queuePosition: 3,
        sequence: 3,
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
      ready: {
        batchId: 'batch-ready',
        memberName: 'Банк Отправка',
        status: 0,
        sequence: 500,
        queuedAt: timestamp,
        updatedAt: timestamp,
        fileCount: 100,
        sentFiles: [],
      },
      delivered: {
        batchId: 'batch-done',
        memberName: 'Банк Готов',
        status: 2,
        sequence: 501,
        queuedAt: timestamp,
        startedAt: timestamp,
        updatedAt: timestamp,
        fileCount: 2,
        sentFiles: [{ filePath: '/safe/done.txt', sentAt: timestamp }],
      },
    },
  };
}
