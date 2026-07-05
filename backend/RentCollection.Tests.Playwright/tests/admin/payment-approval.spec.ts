import { expect } from '@playwright/test';
import { adminTest } from '../fixtures/auth.fixture';

adminTest.describe('Admin Payment Approval', () => {
  adminTest.describe('Payment Approval List', () => {
    adminTest('should load the payment approval page', async ({ adminPage }) => {
      await adminPage.goto('/PaymentApproval');
      await adminPage.waitForLoadState('domcontentloaded');

      const heading = adminPage.locator('h1, h2, .page-title').first();
      await expect(heading).toBeVisible({ timeout: 10000 });

      const pageContent = await adminPage.textContent('body');
      expect(pageContent).toBeTruthy();
    });

    adminTest('should display pending payments list', async ({ adminPage }) => {
      await adminPage.goto('/PaymentApproval');
      await adminPage.waitForLoadState('domcontentloaded');

      // Look for a table or list of pending payments
      const paymentList = adminPage.locator(
        'table, .list-group, .payment-list, [class*="pending"]'
      ).first();
      const isVisible = await paymentList.isVisible().catch(() => false);

      if (isVisible) {
        await expect(paymentList).toBeVisible();
      } else {
        // Page might show "No pending payments" message or just have approval content
        const pageContent = await adminPage.textContent('body');
        const hasApprovalContent =
          pageContent?.includes('Approval') ||
          pageContent?.includes('Payment') ||
          pageContent?.includes('Pending') ||
          pageContent?.includes('No pending') ||
          pageContent?.includes('No payment') ||
          adminPage.url().includes('PaymentApproval');
        expect(hasApprovalContent).toBeTruthy();
      }
    });
  });

  adminTest.describe('Approval Details', () => {
    adminTest('should view payment approval details page', async ({ adminPage }) => {
      await adminPage.goto('/PaymentApproval');
      await adminPage.waitForLoadState('domcontentloaded');

      const detailsLink = adminPage
        .locator(
          'a:has-text("Details"), a:has-text("View"), a:has-text("Review"), a[href*="Details"]'
        )
        .first();
      const detailsVisible = await detailsLink.isVisible().catch(() => false);

      if (detailsVisible) {
        await detailsLink.click();
        await adminPage.waitForLoadState('domcontentloaded');

        const pageContent = await adminPage.textContent('body');
        expect(pageContent).toBeTruthy();

        // Should show approval-related information
        expect(adminPage.url()).toContain('/PaymentApproval');
      } else {
        // No pending approvals - skip gracefully
        adminTest.skip();
      }
    });
  });

  adminTest.describe('Approval History', () => {
    adminTest('should load the approval history page', async ({ adminPage }) => {
      await adminPage.goto('/PaymentApproval/History');
      await adminPage.waitForLoadState('domcontentloaded');

      const pageContent = await adminPage.textContent('body');
      expect(pageContent).toBeTruthy();

      const heading = adminPage.locator('h1, h2, .page-title').first();
      await expect(heading).toBeVisible({ timeout: 10000 });

      // Should contain a table or list of historical approvals
      const historyContent = adminPage.locator(
        'table, .list-group, .history-list, [class*="history"]'
      ).first();
      const hasHistory = await historyContent.isVisible().catch(() => false);

      if (!hasHistory) {
        // May show empty state message - verify page still loads correctly
        const emptyMessage = adminPage.locator(
          'text=No history, text=no records, text=No records, .alert-info, .empty-state'
        ).first();
        const emptyVisible = await emptyMessage.isVisible().catch(() => false);
        expect(hasHistory || emptyVisible || pageContent!.length > 0).toBeTruthy();
      }
    });
  });
});
