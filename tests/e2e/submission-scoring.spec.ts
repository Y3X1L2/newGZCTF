import { test, expect } from '@playwright/test';

test.describe('Submission and Scoring Flow', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/login');
    await page.fill('[name="username"]', 'player1');
    await page.fill('[name="password"]', 'player1');
    await page.click('button[type="submit"]');
    await page.waitForURL('**/dashboard');
  });

  test('player can submit multiple types and see score breakdown', async ({ page }) => {
    await page.goto('/games/1');
    await page.click('[data-testid="challenge-scenario"]');

    // Submit Flag
    await page.click('[data-testid="submission-tab-Flag"]');
    await page.fill('[data-testid="flag-input"]', 'flag{web_entry_point}');
    await page.click('[data-testid="submit-flag"]');
    await expect(page.locator('[data-testid="submission-score-Flag"]')).toContainText('50');

    // Submit IP
    await page.click('[data-testid="submission-tab-IP"]');
    await page.fill('[data-testid="ip-input"]', '192.168.1.100');
    await page.click('[data-testid="submit-ip"]');

    // Upload Writeup
    await page.click('[data-testid="submission-tab-Writeup"]');
    const fileInput = page.locator('[data-testid="writeup-file-input"]');
    await fileInput.setInputFiles({
      name: 'writeup.md',
      mimeType: 'text/markdown',
      buffer: Buffer.from('# 解题报告\n\n## 外网入口\n通过SQL注入获得webshell...')
    });
    await page.click('[data-testid="submit-writeup"]');
    await expect(page.locator('[data-testid="writeup-pending-review"]')).toBeVisible();
  });

  test('leaderboard shows ranked scores with breakdown', async ({ page }) => {
    await page.goto('/games/1/leaderboard');

    await expect(page.locator('[data-testid="leaderboard-table"]')).toBeVisible();
    const rows = page.locator('[data-testid^="leaderboard-row-"]');
    await expect(rows.first()).toBeVisible();

    // First row should show rank 1
    await expect(page.locator('[data-testid="leaderboard-row-0"] [data-testid="rank"]')).toHaveText('1');

    // Detail score columns should be visible
    await expect(page.locator('[data-testid="col-score-Flag"]')).toBeVisible();
    await expect(page.locator('[data-testid="col-score-Writeup"]')).toBeVisible();
    await expect(page.locator('[data-testid="col-score-IP"]')).toBeVisible();
  });

  test('incorrect flag shows error and handles attempt counting', async ({ page }) => {
    await page.goto('/games/1');
    await page.click('[data-testid="challenge-scenario"]');

    await page.click('[data-testid="submission-tab-Flag"]');
    await page.fill('[data-testid="flag-input"]', 'flag{wrong}');
    await page.click('[data-testid="submit-flag"]');

    await expect(page.locator('[data-testid="flag-incorrect"]')).toBeVisible();
    await expect(page.locator('[data-testid="attempt-count"]')).toContainText('1');
  });
});
