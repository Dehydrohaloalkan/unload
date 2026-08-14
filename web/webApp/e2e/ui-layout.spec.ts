import { expect, test } from '@playwright/test';

test.beforeEach(async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('.stage-stack')).toBeVisible();
});

test('centers icons inside icon-only buttons', async ({ page }) => {
  const expectCenteredIcons = async () => {
    const centerOffsets = await page
      .locator('.mat-mdc-icon-button:visible')
      .evaluateAll((buttons) =>
        buttons.map((button) => {
          const icon = button.querySelector<HTMLElement>('.app-icon');
          if (!icon) {
            throw new Error('Visible icon button has no .app-icon element.');
          }

          const buttonRect = button.getBoundingClientRect();
          const iconRect = icon.getBoundingClientRect();
          return iconRect.top + iconRect.height / 2 - (buttonRect.top + buttonRect.height / 2);
        }),
      );

    expect(centerOffsets.length).toBeGreaterThan(0);
    for (const offset of centerOffsets) {
      expect(Math.abs(offset)).toBeLessThanOrEqual(1);
    }
  };

  await expectCenteredIcons();
  await page.locator('app-run-card button[aria-label="Подробнее"]').click();
  await expect(page.locator('.details-drawer--open')).toBeVisible();
  await expectCenteredIcons();
});

test('keeps the header inside the main pane when the drawer opens', async ({ page }) => {
  await page.locator('app-run-card button[aria-label="Подробнее"]').click();
  const drawer = page.locator('.details-drawer--open');
  await expect(drawer).toBeVisible();
  await page.waitForTimeout(300);

  const geometry = await page.evaluate(() => {
    const header = document.querySelector<HTMLElement>('.app-header')!.getBoundingClientRect();
    const drawer = document.querySelector<HTMLElement>('.details-drawer')!.getBoundingClientRect();
    return {
      headerLeft: header.left,
      headerRight: header.right,
      drawerLeft: drawer.left,
      viewportWidth: window.innerWidth,
    };
  });

  expect(geometry.headerLeft).toBeGreaterThanOrEqual(0);
  expect(geometry.headerRight).toBeLessThanOrEqual(geometry.drawerLeft + 1);
  expect(geometry.drawerLeft).toBeLessThan(geometry.viewportWidth);
});

test('presents details as a layered panel tied to the selected stage', async ({ page }) => {
  await page.locator('app-run-card button[aria-label="Подробнее"]').click();
  const drawer = page.locator('.details-drawer--open');
  await expect(drawer).toBeVisible();
  await expect(drawer.locator('.details-drawer__stage-mark')).toHaveText('03');

  const surface = await drawer.evaluate((element) => {
    const style = getComputedStyle(element);
    return {
      backgroundImage: style.backgroundImage,
      borderRadius: Number.parseFloat(style.borderTopLeftRadius),
      boxShadow: style.boxShadow,
    };
  });
  const sectionShadow = await drawer
    .locator('.details-section')
    .first()
    .evaluate((element) => getComputedStyle(element).boxShadow);

  expect(surface.backgroundImage).toContain('radial-gradient');
  expect(surface.borderRadius).toBeGreaterThan(20);
  expect(surface.boxShadow).not.toBe('none');
  expect(sectionShadow).not.toBe('none');
});

test('leaves vertical scrolling to the drawer instead of Material tab internals', async ({
  page,
}) => {
  await page.locator('app-run-card button[aria-label="Подробнее"]').click();
  await page.getByRole('tab', { name: 'История' }).click();
  await expect(page.locator('app-run-history-list')).toBeVisible();

  const nestedOverflow = await page
    .locator(
      '.details-drawer .mat-mdc-tab-body.mat-mdc-tab-body-active, ' +
        '.details-drawer .mat-mdc-tab-body-content',
    )
    .evaluateAll((elements) => elements.map((element) => getComputedStyle(element).overflowY));

  expect(nestedOverflow.length).toBeGreaterThan(0);
  for (const overflow of nestedOverflow) {
    expect(overflow).not.toMatch(/auto|scroll/);
  }

  await expect(page.locator('.details-drawer__body')).toHaveCSS('overflow-y', 'auto');
});

test('gives workflow cards layered depth and a mouse-only hover lift', async ({ page }) => {
  const card = page.locator('.stage-flow__item').first().locator('.mat-mdc-card');
  await expect(card).toBeVisible();

  const restingStyle = await card.evaluate((element) => {
    const style = getComputedStyle(element);
    return { boxShadow: style.boxShadow, transform: style.transform };
  });

  const shadowLayers = restingStyle.boxShadow.split(/, (?=(?:rgba?|color)\()/);
  expect(shadowLayers).toHaveLength(3);

  await card.hover();
  await page.waitForTimeout(220);
  await expect
    .poll(() => card.evaluate((element) => getComputedStyle(element).transform))
    .not.toBe(restingStyle.transform);
});

test('removes decorative card transforms when reduced motion is requested', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.reload();
  await expect(page.locator('.stage-stack')).toBeVisible();

  const card = page.locator('.stage-flow__item').first().locator('.mat-mdc-card');
  await card.hover();
  await expect(card).toHaveCSS('transform', 'none');
});

test.describe('mobile drawer', () => {
  test.use({ viewport: { width: 375, height: 900 } });

  test('covers the viewport without horizontal overflow', async ({ page }) => {
    await page.locator('app-run-card button[aria-label="Подробнее"]').click();
    await expect(page.locator('.details-drawer--open')).toBeVisible();
    await expect(page.locator('.details-drawer--open')).toHaveCSS(
      'transform',
      'matrix(1, 0, 0, 1, 0, 0)',
    );

    const geometry = await page.evaluate(() => {
      const drawer = document
        .querySelector<HTMLElement>('.details-drawer')!
        .getBoundingClientRect();
      return {
        drawerLeft: drawer.left,
        drawerRight: drawer.right,
        viewportWidth: window.innerWidth,
        documentWidth: document.documentElement.scrollWidth,
      };
    });

    expect(geometry.drawerLeft).toBe(0);
    expect(geometry.drawerRight).toBe(geometry.viewportWidth);
    expect(geometry.documentWidth).toBe(geometry.viewportWidth);
  });
});

test.describe('landscape drawer', () => {
  test.use({ viewport: { width: 900, height: 500 } });

  test('fills the viewport and keeps long content on the drawer scroll', async ({ page }) => {
    await page.locator('app-run-card button[aria-label="Подробнее"]').click();
    const drawer = page.locator('.details-drawer--open');
    const body = page.locator('.details-drawer__body');
    await expect(drawer).toBeVisible();
    await expect(drawer).toHaveCSS('transform', 'matrix(1, 0, 0, 1, 0, 0)');

    const geometry = await drawer.evaluate((element) => {
      const rect = element.getBoundingClientRect();
      return { top: rect.top, bottom: rect.bottom, viewportHeight: window.innerHeight };
    });
    expect(geometry.top).toBe(0);
    expect(geometry.bottom).toBe(geometry.viewportHeight);
    await expect(body).toHaveCSS('overflow-y', 'auto');

    await body.hover();
    await page.mouse.wheel(0, 500);
    await expect.poll(() => body.evaluate((element) => element.scrollTop)).toBeGreaterThan(0);
  });
});
