import { expect, test } from '@playwright/test';

test.beforeEach(async ({ page, request }) => {
  await request.post('/api/database/connect', {
    data: { databaseId: 'unload-dev', username: 'playwright', password: 'playwright' },
  });
  await page.goto('/');
  await expect(page.locator('.stage-stack')).toBeVisible();
});

test('shows details immediately in a fixed desktop column and switches the selected stage', async ({
  page,
}) => {
  const panel = page.locator('.details-panel');
  await expect(panel).toBeVisible();
  await expect(panel.locator('.details-panel__stage-mark')).toHaveText('03');

  const geometry = await page.evaluate(() => {
    const workflow = document.querySelector<HTMLElement>('.hero-section')!.getBoundingClientRect();
    const details = document.querySelector<HTMLElement>('.details-panel')!.getBoundingClientRect();
    return {
      workflowRight: workflow.right,
      detailsLeft: details.left,
      documentWidth: document.documentElement.scrollWidth,
      viewportWidth: window.innerWidth,
    };
  });
  expect(geometry.workflowRight).toBeLessThanOrEqual(geometry.detailsLeft + 1);
  expect(geometry.documentWidth).toBe(geometry.viewportWidth);

  await page.locator('app-preset-stage button[aria-label="Подробнее"]').click();
  await expect(panel.locator('.details-panel__stage-mark')).toHaveText('02');
  await expect(panel).toContainText('Детали этапа 2');
});

test('shows plain member states and failure text without the Process tab', async ({ page }) => {
  const snapshot = runSnapshot();
  await page.route('**/api/runs/active', (route) =>
    route.fulfill({ contentType: 'application/json', body: JSON.stringify(snapshot) }),
  );
  await page.route('**/api/runs/run-layout-e2e', (route) =>
    route.fulfill({ contentType: 'application/json', body: JSON.stringify(snapshot) }),
  );
  await page.reload();

  await expect(page.getByRole('tab', { name: 'Процесс' })).toHaveCount(0);
  await expect(page.locator('.member-row').filter({ hasText: 'Успешный банк' })).toContainText(
    'Не в обработке · выгрузился',
  );
  const failed = page.locator('.member-row').filter({ hasText: 'Проблемный банк' });
  await expect(failed).toContainText('Не в обработке · ошибка');
  await expect(failed).toContainText('Нет доступа к каталогу');
  await expect(failed).toContainText('file_write · access-denied');
});

test('restores terminal member results and inline failure details from history after a fresh load', async ({
  page,
}) => {
  const snapshot = historyRunSnapshot();
  await page.route('**/api/runs/today', (route) =>
    route.fulfill({ contentType: 'application/json', body: JSON.stringify([snapshot]) }),
  );
  await page.route('**/api/runs/active', (route) => route.fulfill({ status: 404 }));

  await page.goto('/');
  await expect(page.locator('.stage-stack')).toBeVisible();
  await page.locator('app-run-card button[aria-label="Подробнее"]').click();
  await page.getByRole('tab', { name: 'История' }).click();

  const run = page.locator('.history-run').filter({ hasText: snapshot.correlationId });
  await expect(run).toBeVisible();
  await run.locator('.history-run__summary').click();

  const completedSummary = run
    .locator('.history-member__summary')
    .filter({ hasText: /^Выгрузившийся мембер/ });
  await expect(completedSummary).toContainText('Завершено');

  const failedSummary = run
    .locator('.history-member__summary')
    .filter({ hasText: /^Невыгрузившийся мембер/ });
  const failedMember = failedSummary.locator('..');
  await expect(failedSummary).toContainText('Ошибка');
  await failedSummary.click();
  await expect(failedMember.locator('.history-member__message')).toHaveText(
    'Не удалось записать файл',
  );
  await expect(failedMember.locator('.history-member__context')).toHaveText(
    'file_write · disk-full · FAILED_SCRIPT',
  );
  await expect(failedMember).toContainText('0 файлов.');
});

test.describe('mobile details panel', () => {
  test.use({ viewport: { width: 375, height: 900 } });

  test('stacks below the workflow without overlap or horizontal overflow', async ({ page }) => {
    const geometry = await page.evaluate(() => {
      const workflow = document
        .querySelector<HTMLElement>('.hero-section')!
        .getBoundingClientRect();
      const details = document
        .querySelector<HTMLElement>('.details-panel')!
        .getBoundingClientRect();
      return {
        workflowBottom: workflow.bottom,
        detailsTop: details.top,
        detailsLeft: details.left,
        detailsRight: details.right,
        documentWidth: document.documentElement.scrollWidth,
        viewportWidth: window.innerWidth,
      };
    });

    expect(geometry.detailsTop).toBeGreaterThanOrEqual(geometry.workflowBottom - 1);
    expect(geometry.detailsLeft).toBeGreaterThanOrEqual(0);
    expect(geometry.detailsRight).toBeLessThanOrEqual(geometry.viewportWidth);
    expect(geometry.documentWidth).toBe(geometry.viewportWidth);
  });
});

function runSnapshot() {
  const timestamp = '2026-09-29T10:00:00Z';
  return {
    correlationId: 'run-layout-e2e',
    taskCode: 'run',
    status: 1,
    targetCodes: ['OK', 'FAIL'],
    createdAt: timestamp,
    updatedAt: timestamp,
    message: 'Выгрузка завершена с ошибками',
    memberStatuses: {
      ok: {
        memberName: 'Успешный банк',
        status: 2,
        message: null,
        updatedAt: timestamp,
      },
      failed: {
        memberName: 'Проблемный банк',
        status: 3,
        message: 'Нет доступа к каталогу',
        updatedAt: timestamp,
        failure: {
          stage: 'file_write',
          code: 'access-denied',
          message: 'Нет доступа к каталогу',
          memberName: 'Проблемный банк',
          scriptCode: 'FAILED_SCRIPT',
          filePath: '/output/fail.txt',
          batchId: null,
        },
      },
    },
  };
}

function historyRunSnapshot() {
  const timestamp = '2026-09-29T11:00:00Z';
  return {
    correlationId: 'req-history-result-e2e',
    taskCode: 'run',
    status: 1,
    targetCodes: ['DONE', 'FAILED'],
    createdAt: timestamp,
    updatedAt: timestamp,
    publishToGateway: false,
    memberStatuses: {
      done: {
        memberName: 'Выгрузившийся мембер',
        status: 2,
        message: null,
        updatedAt: timestamp,
      },
      failed: {
        memberName: 'Невыгрузившийся мембер',
        status: 3,
        message: 'Не удалось записать файл',
        updatedAt: timestamp,
        failure: {
          entityType: 'member',
          entityId: 'Невыгрузившийся мембер',
          stage: 'file_write',
          code: 'disk-full',
          message: 'Не удалось записать файл',
          occurredAt: timestamp,
          memberName: 'Невыгрузившийся мембер',
          scriptCode: 'FAILED_SCRIPT',
          workerId: null,
          filePath: null,
          chunkNumber: null,
          batchId: null,
        },
      },
    },
    outputArtifacts: [
      {
        memberName: 'Выгрузившийся мембер',
        scriptCode: 'DONE_SCRIPT',
        fileName: 'done.csv',
        filePath: '/output/done.csv',
        occurredAt: timestamp,
      },
    ],
    senderBatches: {},
  };
}
