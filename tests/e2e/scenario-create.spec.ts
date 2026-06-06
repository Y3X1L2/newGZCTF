import { test, expect } from '@playwright/test';

test.describe('Scenario Creation Flow', () => {
  test.beforeEach(async ({ page }) => {
    // Log in as admin
    await page.goto('/login');
    await page.fill('[name="username"]', 'admin');
    await page.fill('[name="password"]', 'admin');
    await page.click('button[type="submit"]');
    await page.waitForURL('**/dashboard');
  });

  test('admin can create a 3-stage scenario with network rules and scoring config', async ({ page }) => {
    // Navigate to game admin
    await page.goto('/admin/games');
    await page.click('text=新建赛事');

    // Basic scenario info
    await page.fill('[data-testid="scenario-title"]', '企业内网渗透实战');
    await page.fill('[data-testid="scenario-description"]', '从外网Web漏洞入手，逐步渗透至内网域控');

    // Stage 1: 外网入口
    await page.click('[data-testid="add-stage"]');
    await page.fill('[data-testid="stage-title-0"]', '外网入口');
    await page.fill('[data-testid="stage-skill-0"]', '考察Web漏洞利用与信息搜集能力');
    await page.selectOption('[data-testid="stage-image-0"]', { label: 'Web漏洞环境 (Linux)' });
    await page.fill('[data-testid="stage-flag-0"]', 'flag{web_entry_point}');

    // Stage 2: 内网扫描
    await page.click('[data-testid="add-stage"]');
    await page.fill('[data-testid="stage-title-1"]', '内网扫描');
    await page.fill('[data-testid="stage-skill-1"]', '考察内网探测与横向移动能力');
    await page.selectOption('[data-testid="stage-image-1"]', { label: '内网探测环境 (Linux)' });
    await page.fill('[data-testid="stage-flag-1"]', 'flag{internal_scan}');

    // Configure network rule: Stage 2 only accessible from Stage 1
    await page.click('[data-testid="add-network-rule"]');
    await page.selectOption('[data-testid="rule-from-stage"]', { label: '外网入口' });
    await page.selectOption('[data-testid="rule-to-stage"]', { label: '内网扫描' });
    await page.selectOption('[data-testid="rule-action"]', 'Allow');

    // Stage 3: 域控提权
    await page.click('[data-testid="add-stage"]');
    await page.fill('[data-testid="stage-title-2"]', '域控提权');
    await page.fill('[data-testid="stage-skill-2"]', '考察Active Directory渗透与域控提权');
    await page.selectOption('[data-testid="stage-image-2"]', { label: 'Windows域控环境' });
    await page.fill('[data-testid="stage-flag-2"]', 'flag{dc_admin}');

    // Configure scoring rules
    await page.click('[data-testid="scoring-step"]');
    await page.fill('[data-testid="weight-Flag"]', '50');
    await page.fill('[data-testid="weight-Writeup"]', '30');
    await page.fill('[data-testid="weight-IP"]', '20');

    // Submit and verify
    await page.click('[data-testid="submit-scenario"]');
    await expect(page.locator('[data-testid="scenario-created"]')).toBeVisible();
    await expect(page.locator('text=企业内网渗透实战')).toBeVisible();
  });

  test('scenario appears in game challenge list with distinct type badge', async ({ page }) => {
    await page.goto('/games/1');
    await expect(page.locator('[data-testid="challenge-list"]')).toBeVisible();
    // Scenario challenges should have a distinct badge
    const scenarioBadge = page.locator('[data-testid="challenge-type-badge"]:has-text("场景")');
    await expect(scenarioBadge.first()).toBeVisible();
  });
});
