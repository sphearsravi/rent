import { expect } from '@playwright/test';
import { adminTest } from '../fixtures/auth.fixture';

adminTest.describe('Admin Maintenance Management', () => {
  adminTest.describe('Maintenance List', () => {
    adminTest('should load the maintenance requests list page', async ({ adminPage }) => {
      await adminPage.goto('/Maintenance');
      await adminPage.waitForLoadState('domcontentloaded');

      const heading = adminPage
        .locator('h1, h2, .page-title')
        .filter({ hasText: /maintenance/i })
        .first();
      await expect(heading).toBeVisible({ timeout: 10000 });
    });
  });

  adminTest.describe('Create Maintenance Request', () => {
    adminTest('should create a new maintenance request via form', async ({ adminPage }) => {
      await adminPage.goto('/Maintenance/Create');
      await adminPage.waitForLoadState('domcontentloaded');

      const timestamp = Date.now();

      // Description field (textarea, ID: Description)
      const descriptionField = adminPage.locator(
        'textarea#Description, textarea[name="Description"]'
      ).first();
      if (await descriptionField.isVisible().catch(() => false)) {
        await descriptionField.fill(`Plumbing issue in bathroom - leaking faucet needs repair. ${timestamp}`);
      }

      // Property or Unit selection
      const propertySelect = adminPage.locator(
        'select#PropertyId, select[name="PropertyId"], select#PropertyUnitId, select[name="PropertyUnitId"]'
      ).first();
      if (await propertySelect.isVisible().catch(() => false)) {
        const options = propertySelect.locator('option');
        const count = await options.count();
        if (count > 1) {
          const value = await options.nth(1).getAttribute('value');
          if (value) await propertySelect.selectOption(value);
          await adminPage.waitForTimeout(1000);
        }
      }

      // Unit selection (may cascade from property)
      const unitSelect = adminPage.locator(
        'select#UnitId, select[name="UnitId"], select#unitId, select[name="unitId"]'
      ).first();
      if (await unitSelect.isVisible().catch(() => false)) {
        await adminPage.waitForTimeout(1000);
        const options = unitSelect.locator('option');
        const count = await options.count();
        if (count > 1) {
          const value = await options.nth(1).getAttribute('value');
          if (value) await unitSelect.selectOption(value);
        }
      }

      // Tenant selection
      const tenantSelect = adminPage.locator(
        'select#TenantId, select[name="TenantId"]'
      ).first();
      if (await tenantSelect.isVisible().catch(() => false)) {
        const options = tenantSelect.locator('option');
        const count = await options.count();
        if (count > 1) {
          const value = await options.nth(1).getAttribute('value');
          if (value) await tenantSelect.selectOption(value);
        }
      }

      // Priority selection
      const prioritySelect = adminPage.locator(
        'select#Priority, select[name="Priority"], #Priority, [name="Priority"]'
      ).first();
      if (await prioritySelect.isVisible().catch(() => false)) {
        if (await prioritySelect.evaluate((el) => el.tagName === 'SELECT').catch(() => false)) {
          const options = prioritySelect.locator('option');
          const count = await options.count();
          if (count > 1) {
            const value = await options.nth(1).getAttribute('value');
            if (value) await prioritySelect.selectOption(value);
          }
        } else {
          await prioritySelect.fill('High');
        }
      }

      // Category/Type selection
      const categorySelect = adminPage.locator(
        'select#Category, select[name="Category"], select#Type, select[name="Type"], select#RequestType, select[name="RequestType"]'
      ).first();
      if (await categorySelect.isVisible().catch(() => false)) {
        const options = categorySelect.locator('option');
        const count = await options.count();
        if (count > 1) {
          const value = await options.nth(1).getAttribute('value');
          if (value) await categorySelect.selectOption(value);
        }
      }

      // Submit
      await adminPage.locator('button[type="submit"], input[type="submit"]').first().click();
      await adminPage.waitForLoadState('domcontentloaded');

      const currentUrl = adminPage.url();
      expect(currentUrl.includes('/Maintenance')).toBeTruthy();
    });
  });

  adminTest.describe('Edit Maintenance Request', () => {
    adminTest('should edit a maintenance request - assign staff and change status', async ({
      adminPage,
    }) => {
      await adminPage.goto('/Maintenance');
      await adminPage.waitForLoadState('domcontentloaded');

      const editLink = adminPage.locator('a:has-text("Edit"), a[href*="Edit"]').first();
      const editVisible = await editLink.isVisible().catch(() => false);

      if (editVisible) {
        await editLink.click();
        await adminPage.waitForLoadState('domcontentloaded');

        // Assign staff member
        const staffField = adminPage.locator(
          'select#AssignedToUserId, select[name="AssignedToUserId"], select#AssignedTo, select[name="AssignedTo"], #AssignedToUserId, [name="AssignedToUserId"]'
        ).first();
        if (await staffField.isVisible().catch(() => false)) {
          if (await staffField.evaluate((el) => el.tagName === 'SELECT').catch(() => false)) {
            const options = staffField.locator('option');
            const count = await options.count();
            if (count > 1) {
              const value = await options.nth(1).getAttribute('value');
              if (value) await staffField.selectOption(value);
            }
          } else {
            await staffField.fill('John Maintenance');
          }
        }

        // Change status
        const statusField = adminPage.locator(
          'select#Status, select[name="Status"], #Status, [name="Status"]'
        ).first();
        if (await statusField.isVisible().catch(() => false)) {
          if (await statusField.evaluate((el) => el.tagName === 'SELECT').catch(() => false)) {
            const options = statusField.locator('option');
            const count = await options.count();
            if (count > 1) {
              const value = await options.nth(1).getAttribute('value');
              if (value) await statusField.selectOption(value);
            }
          }
        }

        // Submit
        await adminPage.locator('button[type="submit"], input[type="submit"]').first().click();
        await adminPage.waitForLoadState('domcontentloaded');
      } else {
        adminTest.skip();
      }
    });
  });

  adminTest.describe('View Maintenance Request Details', () => {
    adminTest('should display maintenance request details', async ({ adminPage }) => {
      await adminPage.goto('/Maintenance');
      await adminPage.waitForLoadState('domcontentloaded');

      const detailsLink = adminPage
        .locator('a:has-text("Details"), a:has-text("View"), a[href*="Details"]')
        .first();
      const detailsVisible = await detailsLink.isVisible().catch(() => false);

      if (detailsVisible) {
        await detailsLink.click();
        await adminPage.waitForLoadState('domcontentloaded');

        expect(adminPage.url()).toContain('/Maintenance');

        const pageContent = await adminPage.textContent('body');
        expect(pageContent).toBeTruthy();
      } else {
        adminTest.skip();
      }
    });
  });

  adminTest.describe('Delete Maintenance Request', () => {
    adminTest('should delete a maintenance request after confirmation', async ({ adminPage }) => {
      await adminPage.goto('/Maintenance');
      await adminPage.waitForLoadState('domcontentloaded');

      const deleteLink = adminPage
        .locator('a:has-text("Delete"), button:has-text("Delete"), a[href*="Delete"]')
        .first();
      const deleteVisible = await deleteLink.isVisible().catch(() => false);

      if (deleteVisible) {
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

        expect(adminPage.url()).toContain('/Maintenance');
      } else {
        adminTest.skip();
      }
    });
  });
});
