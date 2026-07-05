import { test, expect } from '@playwright/test';
import {
  adminLogin,
  adminLogout,
  ADMIN_EMAIL,
  ADMIN_PASSWORD,
} from '../fixtures/auth.fixture';

const LOGIN_URL = '/Account/Login';

test.describe('Admin Authentication', () => {
  test.describe('Login', () => {
    test('should login with valid credentials and redirect to Dashboard', async ({ page }) => {
      await page.goto(LOGIN_URL);
      await page.waitForLoadState('load');

      const emailField = page.locator('#Email');
      await emailField.waitFor({ state: 'visible' });
      await emailField.click();
      await emailField.fill('');
      await emailField.pressSequentially(ADMIN_EMAIL, { delay: 30 });

      const passwordField = page.locator('#Password');
      await passwordField.waitFor({ state: 'visible' });
      await passwordField.click();
      await passwordField.fill('');
      await passwordField.pressSequentially(ADMIN_PASSWORD, { delay: 30 });

      await page.locator('button[type="submit"], input[type="submit"]').click();
      await page.waitForLoadState('load', { timeout: 15000 });

      // Dashboard is the default route - verify we see Dashboard content
      await expect(page.locator('h1:has-text("Dashboard")')).toBeVisible({ timeout: 15000 });
    });

    test('should show error message with invalid credentials', async ({ page }) => {
      await page.goto(LOGIN_URL);
      await page.waitForLoadState('load');

      await page.locator('#Email').click();
      await page.locator('#Email').fill('');
      await page.locator('#Email').pressSequentially('invalid@test.com', { delay: 30 });
      await page.locator('#Password').click();
      await page.locator('#Password').fill('');
      await page.locator('#Password').pressSequentially('WrongPassword@123', { delay: 30 });
      await page.locator('button[type="submit"], input[type="submit"]').click();
      await page.waitForLoadState('load');

      // Should remain on login page and show an error
      const errorMessage = page.locator(
        '.validation-summary-errors, .text-danger, .alert-danger, [class*="error"], [class*="invalid"]'
      );
      await expect(errorMessage.first()).toBeVisible({ timeout: 10000 });
    });

    test('should show validation errors when fields are empty', async ({ page }) => {
      await page.goto(LOGIN_URL);
      await page.waitForLoadState('load');

      // Attempt to submit with empty fields
      await page.locator('button[type="submit"], input[type="submit"]').click();
      await page.waitForLoadState('load');

      // Check for client-side or server-side validation messages
      const validationMessages = page.locator(
        '.field-validation-error, .validation-summary-errors, .text-danger, [class*="invalid-feedback"], :invalid'
      );
      const count = await validationMessages.count();
      expect(count).toBeGreaterThan(0);
    });
  });

  test.describe('Logout', () => {
    test('should redirect to login page after logout', async ({ page }) => {
      // First login
      await adminLogin(page);

      // Then logout
      await adminLogout(page);
      await page.waitForLoadState('load');

      // Should be redirected to login or home page
      const currentUrl = page.url();
      const isOnAuthPage =
        currentUrl.includes('/Account/Login') ||
        currentUrl.includes('/Account/Logout') ||
        currentUrl.endsWith('/');
      expect(isOnAuthPage).toBeTruthy();
    });
  });

  test.describe('Route Protection', () => {
    test('should redirect to login when accessing Dashboard without authentication', async ({
      page,
    }) => {
      // Try to access dashboard directly without logging in
      await page.goto('/Dashboard');
      await page.waitForLoadState('load');

      // Should be redirected to the login page
      expect(page.url()).toContain('/Account/Login');
    });
  });
});
