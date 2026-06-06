import { test, expect } from '@playwright/test';

test.describe('IR Challenge Flow', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/login');
    await page.fill('[name="username"]', 'admin');
    await page.fill('[name="password"]', 'admin');
    await page.click('button[type="submit"]');
    await page.waitForURL('**/dashboard');
  });

  test('admin can create IR challenge with checkpoints', async ({ page }) => {
    await page.goto('/admin/ir-challenges/new');

    // Basic info
    await page.fill('[data-testid="ir-title"]', '勒索病毒应急响应');
    await page.fill('[data-testid="ir-description"]', '一台Windows Server 2019被勒索病毒感染，数据库被加密...');
    await page.selectOption('[data-testid="ir-os-type"]', 'Windows');

    // Add checkpoints
    // Checkpoint 1: Auto-verify database recovery
    await page.click('[data-testid="add-checkpoint"]');
    await page.fill('[data-testid="checkpoint-desc-0"]', '恢复被加密的PostgreSQL数据库');
    await page.selectOption('[data-testid="checkpoint-verify-type-0"]', 'AutoCommand');
    await page.fill('[data-testid="checkpoint-score-0"]', '100');

    // Checkpoint 2: Manual answer - attacker IP
    await page.click('[data-testid="add-checkpoint"]');
    await page.fill('[data-testid="checkpoint-desc-1"]', '从Web日志中找出攻击者IP');
    await page.selectOption('[data-testid="checkpoint-verify-type-1"]', 'ManualAnswer');
    await page.fill('[data-testid="checkpoint-expected-answer-1"]', '203.0.113.45');
    await page.fill('[data-testid="checkpoint-score-1"]', '50');

    // Checkpoint 3: Manual review - attack path report
    await page.click('[data-testid="add-checkpoint"]');
    await page.fill('[data-testid="checkpoint-desc-2"]', '还原攻击路径（提交报告）');
    await page.selectOption('[data-testid="checkpoint-verify-type-2"]', 'ManualReview');
    await page.fill('[data-testid="checkpoint-score-2"]', '80');

    // Configure scoring
    await page.fill('[data-testid="weight-Flag"]', '40');
    await page.fill('[data-testid="weight-Writeup"]', '30');
    await page.fill('[data-testid="weight-IP"]', '30');

    // Submit
    await page.click('[data-testid="submit-ir-challenge"]');
    await expect(page.locator('[data-testid="ir-created"]')).toBeVisible();
  });

  test('player can access IR environment and view checkpoints', async ({ page }) => {
    // Log in as player
    await page.goto('/login');
    await page.fill('[name="username"]', 'player1');
    await page.fill('[name="password"]', 'player1');
    await page.click('button[type="submit"]');

    // Navigate to IR challenge
    await page.goto('/games/1');
    await page.click('[data-testid="challenge-ir"]');

    // Reserve time slot
    await page.click('[data-testid="timeslot-available"]:first-child');
    await page.click('[data-testid="reserve-slot"]');
    await expect(page.locator('[data-testid="environment-ready"]')).toBeVisible({ timeout: 120000 });

    // Verify checkpoints are displayed
    await expect(page.locator('[data-testid="checkpoint-list"]')).toBeVisible();
    const checkpoints = page.locator('[data-testid^="checkpoint-item-"]');
    await expect(checkpoints).toHaveCount(3);

    // First checkpoint is incomplete
    await expect(page.locator('[data-testid="checkpoint-item-0"]')).toContainText('恢复被加密的PostgreSQL数据库');
    await expect(page.locator('[data-testid="checkpoint-item-0"]')).toContainText('未完成');

    // Submit answer for ManualAnswer checkpoint
    await page.fill('[data-testid="checkpoint-answer-1"]', '203.0.113.45');
    await page.click('[data-testid="submit-checkpoint-1"]');
    await expect(page.locator('[data-testid="checkpoint-item-1"]')).toContainText('已完成');
  });

  test('player can request environment reset', async ({ page }) => {
    await page.goto('/login');
    await page.fill('[name="username"]', 'player1');
    await page.fill('[name="password"]', 'player1');
    await page.click('button[type="submit"]');

    await page.goto('/games/1');
    await page.click('[data-testid="challenge-ir"]');
    await page.click('[data-testid="timeslot-available"]:first-child');
    await page.click('[data-testid="reserve-slot"]');
    await expect(page.locator('[data-testid="environment-ready"]')).toBeVisible({ timeout: 120000 });

    // Click reset button
    await page.click('[data-testid="reset-environment"]');
    await expect(page.locator('[data-testid="reset-confirmation"]')).toBeVisible();
    await page.click('[data-testid="confirm-reset"]');

    // Verify reset in progress
    await expect(page.locator('[data-testid="environment-resetting"]')).toBeVisible();
    await expect(page.locator('[data-testid="environment-ready"]')).toBeVisible({ timeout: 120000 });
  });

  test('admin can perform manual review of writeup submissions', async ({ page }) => {
    await page.goto('/admin/submissions/review');
    await expect(page.locator('[data-testid="pending-reviews"]')).toBeVisible();

    // Click first pending review
    await page.click('[data-testid="review-item"]:first-child');
    await expect(page.locator('[data-testid="submission-content"]')).toBeVisible();

    // Score and comment
    await page.fill('[data-testid="review-score"]', '8');
    await page.fill('[data-testid="review-comment"]', '攻击路径还原较完整，但缺少时间线分析');
    await page.click('[data-testid="submit-review"]');

    await expect(page.locator('[data-testid="review-submitted"]')).toBeVisible();
  });
});
