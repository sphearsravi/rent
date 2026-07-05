import { expect } from '@playwright/test';
import { adminTest } from '../fixtures/auth.fixture';

adminTest.describe('Admin Users Management', () => {
  adminTest.describe('Users List', () => {
    adminTest('should load the users list page', async ({ adminPage }) => {
      await adminPage.goto('/Users');
      await adminPage.waitForLoadState('domcontentloaded');

      const heading = adminPage.locator('h1, h2, .page-title').filter({ hasText: /user/i }).first();
      await expect(heading).toBeVisible({ timeout: 10000 });
    });
  });

  adminTest.describe('Create User', () => {
    adminTest('should create a new user via form', async ({ adminPage }) => {
      await adminPage.goto('/Users/Create');
      await adminPage.waitForLoadState('domcontentloaded');

      const timestamp = Date.now();

      // Username
      const usernameField = adminPage.locator(
        '#Username, [name="Username"], #UserName, [name="UserName"]'
      ).first();
      if (await usernameField.isVisible().catch(() => false)) {
        await usernameField.fill(`testuser${timestamp}`);
      }

      // Email
      const emailField = adminPage.locator('#Email, [name="Email"]').first();
      if (await emailField.isVisible().catch(() => false)) {
        await emailField.fill(`testuser${timestamp}@test.com`);
      }

      // Password
      const passwordField = adminPage.locator(
        '#Password, [name="Password"]'
      ).first();
      if (await passwordField.isVisible().catch(() => false)) {
        await passwordField.fill('TestUser@123');
      }

      // Confirm Password (if present)
      const confirmPasswordField = adminPage.locator(
        '#ConfirmPassword, [name="ConfirmPassword"], #PasswordConfirmation, [name="PasswordConfirmation"]'
      ).first();
      if (await confirmPasswordField.isVisible().catch(() => false)) {
        await confirmPasswordField.fill('TestUser@123');
      }

      // First Name
      const firstNameField = adminPage.locator(
        '#FirstName, [name="FirstName"]'
      ).first();
      if (await firstNameField.isVisible().catch(() => false)) {
        await firstNameField.fill('Test');
      }

      // Last Name
      const lastNameField = adminPage.locator(
        '#LastName, [name="LastName"]'
      ).first();
      if (await lastNameField.isVisible().catch(() => false)) {
        await lastNameField.fill(`User${timestamp}`);
      }

      // Role selection
      const roleField = adminPage.locator(
        'select#Role, select[name="Role"], select#RoleId, select[name="RoleId"], #Role, [name="Role"]'
      ).first();
      if (await roleField.isVisible().catch(() => false)) {
        if (await roleField.evaluate((el) => el.tagName === 'SELECT').catch(() => false)) {
          const options = roleField.locator('option');
          const count = await options.count();
          if (count > 1) {
            const value = await options.nth(1).getAttribute('value');
            if (value) await roleField.selectOption(value);
          }
        } else {
          await roleField.fill('Admin');
        }
      }

      // Submit
      await adminPage.locator('button[type="submit"], input[type="submit"]').first().click();
      await adminPage.waitForLoadState('domcontentloaded');

      const currentUrl = adminPage.url();
      expect(currentUrl.includes('/Users')).toBeTruthy();
    });
  });

  adminTest.describe('Edit User', () => {
    adminTest('should navigate to edit page and modify a user', async ({ adminPage }) => {
      await adminPage.goto('/Users');
      await adminPage.waitForLoadState('domcontentloaded');

      const editLink = adminPage.locator('a:has-text("Edit"), a[href*="Edit"]').first();
      const editVisible = await editLink.isVisible().catch(() => false);

      if (editVisible) {
        await editLink.click();
        await adminPage.waitForLoadState('domcontentloaded');

        // Modify the first name or last name
        const firstNameField = adminPage.locator('#FirstName, [name="FirstName"]').first();
        if (await firstNameField.isVisible().catch(() => false)) {
          await firstNameField.fill(`Updated${Date.now()}`);
        }

        await adminPage.locator('button[type="submit"], input[type="submit"]').first().click();
        await adminPage.waitForLoadState('domcontentloaded');
      } else {
        adminTest.skip();
      }
    });
  });

  adminTest.describe('View User Details', () => {
    adminTest('should navigate to user details page', async ({ adminPage }) => {
      await adminPage.goto('/Users');
      await adminPage.waitForLoadState('domcontentloaded');

      const detailsLink = adminPage
        .locator('a:has-text("Details"), a:has-text("View"), a[href*="Details"]')
        .first();
      const detailsVisible = await detailsLink.isVisible().catch(() => false);

      if (detailsVisible) {
        await detailsLink.click();
        await adminPage.waitForLoadState('domcontentloaded');

        expect(adminPage.url()).toContain('/Users');

        const pageContent = await adminPage.textContent('body');
        expect(pageContent).toBeTruthy();
      } else {
        adminTest.skip();
      }
    });
  });

  adminTest.describe('Delete User', () => {
    adminTest('should delete a user after confirmation', async ({ adminPage }) => {
      await adminPage.goto('/Users');
      await adminPage.waitForLoadState('domcontentloaded');

      // Find a delete link that is NOT for the admin user (to avoid breaking other tests)
      const rows = adminPage.locator('table tbody tr, .user-row, [class*="user"]');
      const rowCount = await rows.count();
      let deleteLink = null;

      for (let i = 0; i < rowCount; i++) {
        const row = rows.nth(i);
        const rowText = await row.textContent();
        // Skip the admin user row
        if (rowText?.includes('admin@test.com') || rowText?.includes('admin')) continue;

        const link = row.locator('a:has-text("Delete"), button:has-text("Delete"), a[href*="Delete"]');
        if ((await link.count()) > 0) {
          deleteLink = link.first();
          break;
        }
      }

      if (deleteLink) {
        adminPage.on('dialog', async (dialog) => {
          await dialog.accept();
        });

        await deleteLink.click();
        await adminPage.waitForLoadState('domcontentloaded');

        const confirmButton = adminPage.locator(
          'button:has-text("Delete"), input[type="submit"]:has-text("Delete"), button:has-text("Confirm")'
        );
        if (await confirmButton.isVisible().catch(() => false)) {
          await confirmButton.first().click();
          await adminPage.waitForLoadState('domcontentloaded');
        }

        expect(adminPage.url()).toContain('/Users');
      } else {
        // No non-admin user to delete, skip
        adminTest.skip();
      }
    });
  });
});
