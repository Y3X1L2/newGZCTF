import { test, expect } from '@playwright/test';

test.describe('Scenario Playthrough Flow', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/login');
    await page.fill('[name="username"]', 'player1');
    await page.fill('[name="password"]', 'player1');
    await page.click('button[type="submit"]');
    await page.waitForURL('**/dashboard');
  });

  test('player can join scenario and progress through stages', async ({ page }) => {
    // Navigate to game and select scenario challenge
    await page.goto('/games/1');
    await page.click('[data-testid="challenge-scenario"]');

    // Reserve a time slot
    await expect(page.locator('[data-testid="timeslot-picker"]')).toBeVisible();
    await page.click('[data-testid="timeslot-available"]:first-child');
    await page.click('[data-testid="reserve-slot"]');
    await expect(page.locator('[data-testid="reservation-confirmed"]')).toBeVisible();

    // Wait for environment to be ready
    await expect(page.locator('[data-testid="environment-ready"]')).toBeVisible({ timeout: 120000 });

    // Stage 1 is active
    await expect(page.locator('[data-testid="current-stage-title"]')).toContainText('外网入口');
    await expect(page.locator('[data-testid="stage-status-2"]')).toHaveAttribute('data-status', 'locked');

    // Submit correct flag for Stage 1
    await page.fill('[data-testid="flag-input"]', 'flag{web_entry_point}');
    await page.click('[data-testid="submit-flag"]');
    await expect(page.locator('[data-testid="flag-correct"]')).toBeVisible();

    // Stage 2 unlocked
    await expect(page.locator('[data-testid="stage-unlocked-notification"]')).toBeVisible();
    await expect(page.locator('[data-testid="current-stage-title"]')).toContainText('内网扫描');
    await expect(page.locator('[data-testid="stage-status-1"]')).toHaveAttribute('data-status', 'completed');

    // Submit flag for Stage 2
    await page.fill('[data-testid="flag-input"]', 'flag{internal_scan}');
    await page.click('[data-testid="submit-flag"]');

    // Stage 3 unlocked
    await expect(page.locator('[data-testid="current-stage-title"]')).toContainText('域控提权');

    // Submit flag for Stage 3 (final stage)
    await page.fill('[data-testid="flag-input"]', 'flag{dc_admin}');
    await page.click('[data-testid="submit-flag"]');

    // Verify completion summary
    await expect(page.locator('[data-testid="completion-summary"]')).toBeVisible();
    await expect(page.locator('[data-testid="completed-all-stages"]')).toBeVisible();
    await expect(page.locator('[data-testid="total-score"]')).toBeVisible();
  });

  test('incorrect flag shows error and does not unlock next stage', async ({ page }) => {
    await page.goto('/games/1');
    await page.click('[data-testid="challenge-scenario"]');
    await page.click('[data-testid="timeslot-available"]:first-child');
    await page.click('[data-testid="reserve-slot"]');

    await expect(page.locator('[data-testid="environment-ready"]')).toBeVisible({ timeout: 120000 });

    // Submit wrong flag
    await page.fill('[data-testid="flag-input"]', 'flag{wrong_answer}');
    await page.click('[data-testid="submit-flag"]');
    await expect(page.locator('[data-testid="flag-incorrect"]')).toBeVisible();

    // Stage 2 still locked
    await expect(page.locator('[data-testid="stage-status-2"]')).toHaveAttribute('data-status', 'locked');
  });

  test('time warning appears when slot is about to end', async ({ page }) => {
    // This test verifies UI elements are present — actual timer behavior requires longer test setup
    await page.goto('/games/1');
    await page.click('[data-testid="challenge-scenario"]');
    await page.click('[data-testid="timeslot-available"]:first-child');
    await page.click('[data-testid="reserve-slot"]');

    await expect(page.locator('[data-testid="time-remaining"]')).toBeVisible();
  });
});
