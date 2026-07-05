import { test as base, expect, Page } from '@playwright/test';

// Admin credentials
export const ADMIN_EMAIL = 'admin@test.com';
export const ADMIN_PASSWORD = 'Admin@123';

// Tenant credentials (demo)
export const TENANT_CIVIL_ID = 'ID-265234567';
export const TENANT_PASSWORD = 'Welcome@123';

export async function adminLogin(page: Page) {
  await page.goto('/Account/Login');
  await page.waitForLoadState('load');

  // Wait for and fill form fields using pressSequentially to work with jQuery Validation
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

  // Submit and wait for navigation
  await page.locator('button[type="submit"], input[type="submit"]').click();
  await page.waitForLoadState('load', { timeout: 15000 });

  // Verify login succeeded - should no longer be on login page
  await page.waitForTimeout(1000);
  if (page.url().includes('/Account/Login')) {
    throw new Error('Admin login failed - still on login page. Check credentials.');
  }
}

export async function tenantLogin(page: Page, civilId = TENANT_CIVIL_ID, password = TENANT_PASSWORD) {
  await page.goto('/TenantPortal/Login');
  await page.waitForLoadState('load');

  const civilIdField = page.locator('#civilId');
  await civilIdField.waitFor({ state: 'visible' });
  await civilIdField.click();
  await civilIdField.fill('');
  await civilIdField.pressSequentially(civilId, { delay: 30 });

  const passwordField = page.locator('#password');
  await passwordField.waitFor({ state: 'visible' });
  await passwordField.click();
  await passwordField.fill('');
  await passwordField.pressSequentially(password, { delay: 30 });

  await page.locator('button[type="submit"], input[type="submit"]').click();
  await page.waitForLoadState('load', { timeout: 15000 });

  // Verify login succeeded - should no longer be on login page
  await page.waitForTimeout(1000);
  if (page.url().includes('/TenantPortal/Login')) {
    throw new Error('Tenant login failed - still on login page. Check credentials.');
  }
}

export async function adminLogout(page: Page) {
  await page.goto('/Account/Logout');
}

export async function tenantLogout(page: Page) {
  await page.goto('/TenantPortal/Logout');
}

// Extended test fixture with pre-authenticated admin
export const adminTest = base.extend<{ adminPage: Page }>({
  adminPage: async ({ page }, use) => {
    await adminLogin(page);
    await use(page);
  },
});

// Extended test fixture with pre-authenticated tenant
export const tenantTest = base.extend<{ tenantPage: Page }>({
  tenantPage: async ({ page }, use) => {
    await tenantLogin(page);
    await use(page);
  },
});
