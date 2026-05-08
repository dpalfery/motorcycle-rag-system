import { test, expect } from '@playwright/test';

test.describe('Motorcycle RAG Web UI - Smoke Tests', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('https://localhost:5173');
  });

  test('should load the login page', async ({ page }) => {
    await expect(page).toHaveTitle(/MotorcycleRag/);
    await expect(page.locator('text=Sign In')).toBeVisible();
  });

  test('should have sign in button', async ({ page }) => {
    const signInButton = page.locator('button:has-text("Sign In")');
    await expect(signInButton).toBeVisible();
  });

  test('should redirect to auth on login click', async ({ page }) => {
    const signInButton = page.locator('button:has-text("Sign In")');
    await signInButton.click();
    await page.waitForTimeout(2000);
    expect(page.url()).toContain('palfery.ciamlogin.com');
  });
});
