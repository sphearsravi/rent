import { expect } from '@playwright/test';
import { adminTest } from '../fixtures/auth.fixture';

adminTest.describe('Admin Payments Management', () => {
  adminTest.describe('Payments List', () => {
    adminTest.beforeEach(async ({ adminPage }) => {
      await adminPage.goto('/Payments');
      await adminPage.waitForLoadState('domcontentloaded');
    });

    adminTest('should load the payments list page with summary cards', async ({ adminPage }) => {
      const heading = adminPage.locator('h1, h2, .page-title').filter({ hasText: /payments/i }).first();
      await expect(heading).toBeVisible({ timeout: 10000 });

      // Check for summary/stat cards on the payments page
      const summaryCards = adminPage.locator('.card, .stat-card, .summary-card');
      const cardCount = await summaryCards.count();
      expect(cardCount).toBeGreaterThan(0);
    });

    adminTest('should filter payments by property dropdown', async ({ adminPage }) => {
      const propertyFilter = adminPage.locator(
        'select[name="propertyId"], select#propertyFilter, select[name="propertyFilter"], select[id*="property"], select[id*="Property"]'
      ).first();
      const isVisible = await propertyFilter.isVisible().catch(() => false);

      if (isVisible) {
        const options = propertyFilter.locator('option');
        const optionCount = await options.count();
        if (optionCount > 1) {
          const secondOptionValue = await options.nth(1).getAttribute('value');
          if (secondOptionValue) {
            await propertyFilter.selectOption(secondOptionValue);
            await adminPage.waitForTimeout(1000);
            await adminPage.waitForLoadState('domcontentloaded');
          }
        }
      } else {
        adminTest.skip();
      }
    });

    adminTest('should filter payments by status dropdown', async ({ adminPage }) => {
      const statusFilter = adminPage.locator(
        'select[name="status"], select#statusFilter, select[name="statusFilter"], select[id*="status"], select[id*="Status"]'
      ).first();
      const isVisible = await statusFilter.isVisible().catch(() => false);

      if (isVisible) {
        const options = statusFilter.locator('option');
        const optionCount = await options.count();
        if (optionCount > 1) {
          const secondOptionValue = await options.nth(1).getAttribute('value');
          if (secondOptionValue) {
            await statusFilter.selectOption(secondOptionValue);
            await adminPage.waitForTimeout(1000);
            await adminPage.waitForLoadState('domcontentloaded');
          }
        }
      } else {
        adminTest.skip();
      }
    });
  });

  adminTest.describe('Payment Mismatches', () => {
    adminTest('should load the payment mismatches page', async ({ adminPage }) => {
      await adminPage.goto('/Payments/Mismatches');
      await adminPage.waitForLoadState('domcontentloaded');

      // Page should load successfully (200 status or visible content)
      const pageContent = await adminPage.textContent('body');
      expect(pageContent).toBeTruthy();

      // Look for mismatch-related heading or content
      const heading = adminPage.locator(
        'h1, h2, .page-title'
      ).first();
      await expect(heading).toBeVisible({ timeout: 10000 });
    });
  });

  adminTest.describe('View Payment Details', () => {
    adminTest('should navigate to payment details page', async ({ adminPage }) => {
      await adminPage.goto('/Payments');
      await adminPage.waitForLoadState('domcontentloaded');

      const detailsLink = adminPage
        .locator('a:has-text("Details"), a:has-text("View"), a[href*="Details"]')
        .first();
      const detailsVisible = await detailsLink.isVisible().catch(() => false);

      if (detailsVisible) {
        await detailsLink.click();
        await adminPage.waitForLoadState('domcontentloaded');

        expect(adminPage.url()).toContain('/Payments');
        const pageContent = await adminPage.textContent('body');
        expect(pageContent).toBeTruthy();
      } else {
        // No payments to view - skip gracefully
        adminTest.skip();
      }
    });
  });

  adminTest.describe('Payment Reconciliation', () => {
    adminTest('should load the reconciliation page', async ({ adminPage }) => {
      await adminPage.goto('/Payments/Reconciliation');
      await adminPage.waitForLoadState('domcontentloaded');

      const pageContent = await adminPage.textContent('body');
      expect(pageContent).toBeTruthy();

      const heading = adminPage.locator(
        'h1, h2, .page-title'
      ).first();
      await expect(heading).toBeVisible({ timeout: 10000 });
    });
  });
});
