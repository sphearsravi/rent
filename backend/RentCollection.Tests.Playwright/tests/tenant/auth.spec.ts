import { test, expect } from '@playwright/test';
import {
  tenantLogin,
  tenantLogout,
  TENANT_CIVIL_ID,
  TENANT_PASSWORD,
} from '../fixtures/auth.fixture';

const LOGIN_URL = '/TenantPortal/Login';
const DASHBOARD_URL = '/TenantPortal/Dashboard';

test.describe('Tenant Authentication', () => {
  test.describe('Login', () => {
    test('should login with valid Civil ID and redirect to Dashboard', async ({ page }) => {
      await page.goto(LOGIN_URL);
      await page.waitForLoadState('load');

      const civilIdField = page.locator('#civilId');
      await civilIdField.waitFor({ state: 'visible' });
      await civilIdField.click();
      await civilIdField.fill('');
      await civilIdField.pressSequentially(TENANT_CIVIL_ID, { delay: 30 });

      const passwordField = page.locator('#password');
      await passwordField.waitFor({ state: 'visible' });
      await passwordField.click();
      await passwordField.fill('');
      await passwordField.pressSequentially(TENANT_PASSWORD, { delay: 30 });

      await page.locator('button[type="submit"], input[type="submit"]').click();
      await page.waitForLoadState('load', { timeout: 15000 });

      // Should be redirected away from login page
      await page.waitForTimeout(1000);
      expect(page.url()).not.toContain('/TenantPortal/Login');
    });

    test('should show error message with invalid Civil ID', async ({ page }) => {
      await page.goto(LOGIN_URL);
      await page.waitForLoadState('domcontentloaded');

      await page.locator('#civilId').click();
      await page.locator('#civilId').fill('');
      await page.locator('#civilId').pressSequentially('INVALID-999999', { delay: 30 });
      await page.locator('#password').click();
      await page.locator('#password').fill('');
      await page.locator('#password').pressSequentially(TENANT_PASSWORD, { delay: 30 });
      await page.locator('button[type="submit"], input[type="submit"]').click();

      await page.waitForLoadState('domcontentloaded');

      // Should remain on login page and show an error
      const errorMessage = page.locator(
        '.validation-summary-errors, .text-danger, .alert-danger, [class*="error"], [class*="invalid"]'
      );
      await expect(errorMessage.first()).toBeVisible({ timeout: 10000 });

      // Verify error text indicates invalid credentials
      const pageContent = await page.textContent('body');
      expect(pageContent).toContain('Invalid Civil ID or password');
    });

    test('should show generic error with wrong password (no user enumeration)', async ({
      page,
    }) => {
      await page.goto(LOGIN_URL);
      await page.waitForLoadState('domcontentloaded');

      await page.locator('#civilId').click();
      await page.locator('#civilId').fill('');
      await page.locator('#civilId').pressSequentially(TENANT_CIVIL_ID, { delay: 30 });
      await page.locator('#password').click();
      await page.locator('#password').fill('');
      await page.locator('#password').pressSequentially('WrongPassword@999', { delay: 30 });
      await page.locator('button[type="submit"], input[type="submit"]').click();

      await page.waitForLoadState('domcontentloaded');

      // Should show same generic error message as invalid Civil ID
      const errorMessage = page.locator(
        '.validation-summary-errors, .text-danger, .alert-danger, [class*="error"], [class*="invalid"]'
      );
      await expect(errorMessage.first()).toBeVisible({ timeout: 10000 });

      // Same generic error - no user enumeration
      const pageContent = await page.textContent('body');
      expect(pageContent).toContain('Invalid Civil ID or password');
    });

    test('should have Demo Login button that fills credentials', async ({ page }) => {
      await page.goto(LOGIN_URL);
      await page.waitForLoadState('domcontentloaded');

      // Find and click the demo login button
      const demoButton = page.locator(
        'button:has-text("Demo"), a:has-text("Demo"), [id*="demo"], [class*="demo"]'
      );
      await expect(demoButton.first()).toBeVisible({ timeout: 5000 });
      await demoButton.first().click();

      // Wait a moment for fields to be filled
      await page.waitForTimeout(500);

      // Verify the credentials were filled in
      const civilIdValue = await page.locator('#civilId').inputValue();
      const passwordValue = await page.locator('#password').inputValue();

      expect(civilIdValue).toBe(TENANT_CIVIL_ID);
      expect(passwordValue).toBe(TENANT_PASSWORD);
    });

    test('should have Remember Me checkbox', async ({ page }) => {
      await page.goto(LOGIN_URL);
      await page.waitForLoadState('domcontentloaded');

      const rememberMe = page.locator(
        'input[type="checkbox"][name*="emember"], input[type="checkbox"]#rememberMe, input[type="checkbox"][id*="emember"]'
      );
      await expect(rememberMe.first()).toBeVisible({ timeout: 5000 });
    });
  });

  test.describe('Logout', () => {
    test('should redirect to Login page after logout', async ({ page }) => {
      // First login
      await tenantLogin(page);
      expect(page.url()).not.toContain('/TenantPortal/Login');

      // Then logout
      await tenantLogout(page);
      await page.waitForLoadState('domcontentloaded');

      // Should be redirected to tenant login page
      const currentUrl = page.url();
      const isOnLoginPage =
        currentUrl.includes('/TenantPortal/Login') ||
        currentUrl.includes('/TenantPortal/Logout') ||
        currentUrl.endsWith('/');
      expect(isOnLoginPage).toBeTruthy();
    });
  });

  test.describe('Route Protection', () => {
    test('should redirect to Login when accessing Dashboard without authentication', async ({
      page,
    }) => {
      // Try to access dashboard directly without logging in
      await page.goto(DASHBOARD_URL);
      await page.waitForLoadState('domcontentloaded');

      // Should be redirected to a login page (tenant or admin)
      const currentUrl = page.url();
      const isRedirectedToLogin =
        currentUrl.includes('/TenantPortal/Login') ||
        currentUrl.includes('/Account/Login');
      expect(isRedirectedToLogin).toBeTruthy();
    });
  });
});
