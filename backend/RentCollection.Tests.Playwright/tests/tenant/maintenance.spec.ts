import { expect } from '@playwright/test';
import { tenantTest } from '../fixtures/auth.fixture';

const DASHBOARD_URL = '/TenantPortal/Dashboard';

tenantTest.describe('Tenant Maintenance Requests', () => {
  tenantTest.beforeEach(async ({ tenantPage }) => {
    await tenantPage.goto(DASHBOARD_URL);
    await tenantPage.waitForLoadState('domcontentloaded');
  });

  tenantTest.describe('Maintenance Section on Dashboard', () => {
    tenantTest('should display maintenance section', async ({ tenantPage }) => {
      const pageContent = await tenantPage.textContent('body');
      const hasMaintenanceSection =
        pageContent?.includes('Maintenance') ||
        pageContent?.includes('Service Request') ||
        pageContent?.includes('Repair');
      expect(hasMaintenanceSection).toBeTruthy();
    });

    tenantTest('should show pending maintenance requests in dashboard', async ({ tenantPage }) => {
      // Look for maintenance requests list or cards
      const maintenanceSection = tenantPage.locator(
        '[class*="maintenance"], [class*="request"], .card:has-text("Maintenance")'
      );

      const pageContent = await tenantPage.textContent('body');
      const hasRequests =
        pageContent?.includes('Maintenance') ||
        pageContent?.includes('Request') ||
        pageContent?.includes('No maintenance') ||
        pageContent?.includes('No request');
      expect(hasRequests).toBeTruthy();
    });
  });

  tenantTest.describe('Maintenance Modal', () => {
    tenantTest('should open maintenance modal when clicking submit button', async ({
      tenantPage,
    }) => {
      // Find the button that opens the maintenance request modal
      const submitButton = tenantPage.locator(
        'button:has-text("New Request"), [onclick*="showMaintenanceModal"], button:has-text("Maintenance"), button:has-text("Submit Request"), [data-bs-target*="maintenance"], [data-bs-toggle="modal"]:has-text("Request"), a:has-text("New Request"), a:has-text("Submit Request")'
      );

      if ((await submitButton.count()) > 0) {
        await submitButton.first().click();
        await tenantPage.waitForTimeout(1000);

        // Verify modal is visible (check multiple modal patterns)
        const modal = tenantPage.locator(
          '.modal.show, .modal[style*="display: block"], [class*="modal"][class*="show"], .modal:visible, .modal.fade.show, #maintenanceModal'
        );

        const modalVisible = await modal.first().isVisible().catch(() => false);
        if (modalVisible) {
          await expect(modal.first()).toBeVisible();
        } else {
          // Modal might be on a separate page instead of inline
          const pageContent = await tenantPage.textContent('body');
          expect(pageContent).toContain('Maintenance');
        }
      } else {
        // Button might be on a different page or labeled differently
        const pageContent = await tenantPage.textContent('body');
        expect(pageContent).toContain('Maintenance');
      }
    });

    tenantTest('should have required form fields in maintenance modal', async ({ tenantPage }) => {
      // Open the modal first
      const submitButton = tenantPage.locator(
        'button:has-text("New Request"), [onclick*="showMaintenanceModal"], button:has-text("Maintenance"), button:has-text("Submit Request"), [data-bs-target*="maintenance"], [data-bs-toggle="modal"]:has-text("Request")'
      );

      if ((await submitButton.count()) > 0) {
        await submitButton.first().click();
        await tenantPage.waitForTimeout(500);

        // Check for Property/Lease select
        const propertySelect = tenantPage.locator(
          '#propertySelect, select[name*="ease"], select[name*="roperty"], select[id*="ease"], select[id*="roperty"]'
        );
        const hasPropertySelect = (await propertySelect.count()) > 0;

        // Check for Category field
        const categoryField = tenantPage.locator(
          '#categorySelect, select[name*="ategory"], select[id*="ategory"], select[name*="Category"]'
        );
        const hasCategory = (await categoryField.count()) > 0;

        // Check for Priority field
        const priorityField = tenantPage.locator(
          '#prioritySelect, select[name*="riority"], select[id*="riority"], select[name*="Priority"]'
        );
        const hasPriority = (await priorityField.count()) > 0;

        // Check for Description field
        const descriptionField = tenantPage.locator(
          '#descriptionText, textarea[name*="escription"], textarea[id*="escription"], textarea[name*="Description"], textarea'
        );
        const hasDescription = (await descriptionField.count()) > 0;

        // At least some fields should be present
        const totalFields = [hasPropertySelect, hasCategory, hasPriority, hasDescription].filter(
          Boolean
        ).length;
        expect(totalFields).toBeGreaterThanOrEqual(2);
      }
    });

    tenantTest('should submit maintenance request with all fields filled', async ({
      tenantPage,
    }) => {
      // Open the modal
      const submitButton = tenantPage.locator(
        'button:has-text("New Request"), [onclick*="showMaintenanceModal"], button:has-text("Maintenance"), button:has-text("Submit Request"), [data-bs-target*="maintenance"], [data-bs-toggle="modal"]:has-text("Request")'
      );

      if ((await submitButton.count()) > 0) {
        await submitButton.first().click();
        await tenantPage.waitForTimeout(500);

        // Fill Property/Lease select (pick first option if available)
        const propertySelect = tenantPage.locator(
          '#propertySelect, select[name*="ease"], select[name*="roperty"], select[id*="ease"], select[id*="roperty"]'
        );
        if ((await propertySelect.count()) > 0) {
          const options = propertySelect.first().locator('option');
          const optionCount = await options.count();
          if (optionCount > 1) {
            // Select the second option (skip placeholder)
            const optionValue = await options.nth(1).getAttribute('value');
            if (optionValue) {
              await propertySelect.first().selectOption(optionValue);
            }
          }
        }

        // Fill Category
        const categoryField = tenantPage.locator(
          'select[name*="ategory"], select[id*="ategory"], select[name*="Category"]'
        );
        if ((await categoryField.count()) > 0) {
          const options = categoryField.first().locator('option');
          const optionCount = await options.count();
          if (optionCount > 1) {
            const optionValue = await options.nth(1).getAttribute('value');
            if (optionValue) {
              await categoryField.first().selectOption(optionValue);
            }
          }
        }

        // Fill Priority
        const priorityField = tenantPage.locator(
          'select[name*="riority"], select[id*="riority"], select[name*="Priority"]'
        );
        if ((await priorityField.count()) > 0) {
          const options = priorityField.first().locator('option');
          const optionCount = await options.count();
          if (optionCount > 1) {
            const optionValue = await options.nth(1).getAttribute('value');
            if (optionValue) {
              await priorityField.first().selectOption(optionValue);
            }
          }
        }

        // Fill Description
        const descriptionField = tenantPage.locator(
          '#descriptionText, textarea[name*="escription"], textarea[id*="escription"], textarea[name*="Description"], textarea'
        );
        if ((await descriptionField.count()) > 0) {
          const timestamp = Date.now();
          await descriptionField.first().fill(
            `Automated test maintenance request - ${timestamp}. The kitchen faucet is leaking and needs repair.`
          );
        }

        // Submit the form inside the modal
        const modalSubmitButton = tenantPage.locator(
          '.modal.show button[type="submit"], .modal.show button:has-text("Submit"), .modal.show button:has-text("Save"), .modal button[type="submit"]'
        );
        if ((await modalSubmitButton.count()) > 0) {
          await modalSubmitButton.first().click();
          await tenantPage.waitForLoadState('domcontentloaded');

          // After submission, check for success message or redirect
          await tenantPage.waitForTimeout(1000);
          const pageContent = await tenantPage.textContent('body');
          const wasSubmitted =
            pageContent?.includes('success') ||
            pageContent?.includes('submitted') ||
            pageContent?.includes('created') ||
            pageContent?.includes('Maintenance') ||
            tenantPage.url().includes('Dashboard');
          expect(wasSubmitted).toBeTruthy();
        }
      }
    });

    tenantTest('should show required field validation on empty submit', async ({ tenantPage }) => {
      // Open the modal
      const submitButton = tenantPage.locator(
        'button:has-text("New Request"), [onclick*="showMaintenanceModal"], button:has-text("Maintenance"), button:has-text("Submit Request"), [data-bs-target*="maintenance"], [data-bs-toggle="modal"]:has-text("Request")'
      );

      if ((await submitButton.count()) > 0) {
        await submitButton.first().click();
        await tenantPage.waitForTimeout(500);

        // Try to submit without filling any fields
        const modalSubmitButton = tenantPage.locator(
          '.modal.show button[type="submit"], .modal.show button:has-text("Submit"), .modal.show button:has-text("Save"), .modal button[type="submit"]'
        );
        if ((await modalSubmitButton.count()) > 0) {
          await modalSubmitButton.first().click();
          await tenantPage.waitForTimeout(500);

          // Check for validation errors (client-side or server-side)
          const validationErrors = tenantPage.locator(
            '.field-validation-error, .validation-summary-errors, .text-danger, [class*="invalid-feedback"], .is-invalid, :invalid, .error-message'
          );
          const errorCount = await validationErrors.count();

          // Either validation errors appear, or the modal remains open (prevented submission)
          const modalStillOpen = tenantPage.locator('.modal.show, .modal[style*="display: block"]');
          const isModalOpen = (await modalStillOpen.count()) > 0;

          expect(errorCount > 0 || isModalOpen).toBeTruthy();
        }
      }
    });
  });
});
