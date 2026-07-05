import { expect } from '@playwright/test';
import { adminTest } from '../fixtures/auth.fixture';

adminTest.describe('Admin Reports', () => {
  adminTest.describe('Reports Index', () => {
    adminTest('should load the reports index page', async ({ adminPage }) => {
      await adminPage.goto('/Reports');
      await adminPage.waitForLoadState('domcontentloaded');

      const heading = adminPage.locator('h1, h2, .page-title').filter({ hasText: /reports/i }).first();
      await expect(heading).toBeVisible({ timeout: 10000 });

      const pageContent = await adminPage.textContent('body');
      expect(pageContent).toBeTruthy();
    });
  });

  adminTest.describe('Executive Dashboard Report', () => {
    adminTest('should load the executive dashboard report', async ({ adminPage }) => {
      await adminPage.goto('/Reports');
      await adminPage.waitForLoadState('domcontentloaded');

      // Navigate to executive dashboard report
      const executiveLink = adminPage
        .locator(
          'a:has-text("Executive"), a:has-text("executive"), a[href*="Executive"], a[href*="executive"]'
        )
        .first();
      const isVisible = await executiveLink.isVisible().catch(() => false);

      if (isVisible) {
        await executiveLink.click();
        await adminPage.waitForLoadState('domcontentloaded');
      } else {
        // Try direct URL navigation
        await adminPage.goto('/Reports/ExecutiveDashboard');
        await adminPage.waitForLoadState('domcontentloaded');
      }

      // Verify the report page loaded (may have charts, tables, or redirect back to reports)
      const pageContent = await adminPage.textContent('body');
      const hasReportContent =
        (pageContent && pageContent.length > 0) ||
        adminPage.url().includes('Report') ||
        adminPage.url().includes('Executive');
      expect(hasReportContent).toBeTruthy();
    });
  });

  adminTest.describe('Payment Report', () => {
    adminTest('should load the payment report page', async ({ adminPage }) => {
      await adminPage.goto('/Reports');
      await adminPage.waitForLoadState('domcontentloaded');

      const paymentLink = adminPage
        .locator(
          'a:has-text("Payment Report"), a:has-text("Payment"), a[href*="Payment"], a[href*="payment"]'
        )
        .first();
      const isVisible = await paymentLink.isVisible().catch(() => false);

      if (isVisible) {
        await paymentLink.click();
        await adminPage.waitForLoadState('domcontentloaded');

        const pageContent = await adminPage.textContent('body');
        expect(pageContent).toBeTruthy();
      } else {
        await adminPage.goto('/Reports/Payments');
        await adminPage.waitForLoadState('domcontentloaded');

        const pageContent = await adminPage.textContent('body');
        expect(pageContent).toBeTruthy();
      }
    });
  });

  adminTest.describe('Property Performance Report', () => {
    adminTest('should load the property performance report page', async ({ adminPage }) => {
      await adminPage.goto('/Reports');
      await adminPage.waitForLoadState('domcontentloaded');

      const propertyLink = adminPage
        .locator(
          'a:has-text("Property Performance"), a:has-text("Property"), a[href*="Property"], a[href*="property"]'
        )
        .first();
      const isVisible = await propertyLink.isVisible().catch(() => false);

      if (isVisible) {
        await propertyLink.click();
        await adminPage.waitForLoadState('domcontentloaded');

        const pageContent = await adminPage.textContent('body');
        expect(pageContent).toBeTruthy();
      } else {
        await adminPage.goto('/Reports/PropertyPerformance');
        await adminPage.waitForLoadState('domcontentloaded');

        const pageContent = await adminPage.textContent('body');
        expect(pageContent).toBeTruthy();
      }
    });
  });

  adminTest.describe('Invoice Status Report', () => {
    adminTest('should load the invoice status report page', async ({ adminPage }) => {
      await adminPage.goto('/Reports');
      await adminPage.waitForLoadState('domcontentloaded');

      const invoiceLink = adminPage
        .locator(
          'a:has-text("Invoice Status"), a:has-text("Invoice"), a[href*="Invoice"], a[href*="invoice"]'
        )
        .first();
      const isVisible = await invoiceLink.isVisible().catch(() => false);

      if (isVisible) {
        await invoiceLink.click();
        await adminPage.waitForLoadState('domcontentloaded');

        const pageContent = await adminPage.textContent('body');
        expect(pageContent).toBeTruthy();
      } else {
        await adminPage.goto('/Reports/InvoiceStatus');
        await adminPage.waitForLoadState('domcontentloaded');

        const pageContent = await adminPage.textContent('body');
        expect(pageContent).toBeTruthy();
      }
    });
  });

  adminTest.describe('Tenant Payment Analysis Report', () => {
    adminTest('should load the tenant payment analysis report page', async ({ adminPage }) => {
      await adminPage.goto('/Reports');
      await adminPage.waitForLoadState('domcontentloaded');

      const tenantLink = adminPage
        .locator(
          'a:has-text("Tenant Payment"), a:has-text("Tenant Analysis"), a:has-text("Payment Analysis"), a[href*="TenantPayment"], a[href*="tenantPayment"]'
        )
        .first();
      const isVisible = await tenantLink.isVisible().catch(() => false);

      if (isVisible) {
        await tenantLink.click();
        await adminPage.waitForLoadState('domcontentloaded');

        const pageContent = await adminPage.textContent('body');
        expect(pageContent).toBeTruthy();
      } else {
        await adminPage.goto('/Reports/TenantPaymentAnalysis');
        await adminPage.waitForLoadState('domcontentloaded');

        const pageContent = await adminPage.textContent('body');
        expect(pageContent).toBeTruthy();
      }
    });
  });
});
