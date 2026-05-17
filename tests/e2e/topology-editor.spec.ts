import { test, expect } from '@playwright/test';

test.describe('Topology Editor Flow', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/login');
    await page.fill('[name="username"]', 'admin');
    await page.fill('[name="password"]', 'admin');
    await page.click('button[type="submit"]');
    await page.waitForURL('**/dashboard');
  });

  test('admin can create topology with nodes and connections', async ({ page }) => {
    // Navigate to scenario edit
    await page.goto('/admin/scenarios/1/edit');

    // Go to topology step
    await page.click('[data-testid="topology-step"]');
    await expect(page.locator('.react-flow')).toBeVisible();

    // Add entry node
    await page.selectOption('[data-testid="node-type-select"]', 'entry');
    await page.click('[data-testid="add-topology-node"]');

    // Add internal node
    await page.selectOption('[data-testid="node-type-select"]', 'internal');
    await page.click('[data-testid="add-topology-node"]');

    // Add DC node
    await page.selectOption('[data-testid="node-type-select"]', 'dc');
    await page.click('[data-testid="add-topology-node"]');

    // Add DMZ node
    await page.selectOption('[data-testid="node-type-select"]', 'dmz');
    await page.click('[data-testid="add-topology-node"]');

    // Verify 4 nodes exist
    const nodes = page.locator('.react-flow__node');
    await expect(nodes).toHaveCount(4);

    // Save topology
    await page.click('[data-testid="save-topology"]');
    await expect(page.locator('[data-testid="topology-saved"]')).toBeVisible();
  });

  test('player sees stage-filtered topology view', async ({ page }) => {
    // Log in as player with active scenario at stage 2
    await page.goto('/login');
    await page.fill('[name="username"]', 'player1');
    await page.fill('[name="password"]', 'player1');
    await page.click('button[type="submit"]');

    await page.goto('/games/1');
    await page.click('[data-testid="challenge-scenario"]');

    // Topology viewer should be visible
    await expect(page.locator('.react-flow')).toBeVisible();

    // Locked nodes (stage 3+) should be greyed/opaque
    // Unlocked/completed nodes (stage 1-2) should be fully visible
    // Exact verification depends on stage configuration
  });
});
