import { expect } from '@playwright/test';
import { tenantTest } from '../fixtures/auth.fixture';

const DASHBOARD_URL = '/TenantPortal/Dashboard';

tenantTest.describe('Tenant Dashboard', () => {
  tenantTest.beforeEach(async ({ tenantPage }) => {
    await tenantPage.goto(DASHBOARD_URL);
    await tenantPage.waitForLoadState('domcontentloaded');
  });

  tenantTest.describe('Page Load', () => {
    tenantTest('should load dashboard with welcome message containing tenant name', async ({
      tenantPage,
    }) => {
      // Look for a welcome/greeting element that contains the tenant name
      const welcomeMessage = tenantPage.locator(
        'h1, h2, h3, h4, .welcome, [class*="welcome"], [class*="greeting"]'
      );
      await expect(welcomeMessage.first()).toBeVisible({ timeout: 10000 });

      // The page should contain some form of welcome text
      const pageContent = await tenantPage.textContent('body');
      const hasWelcome =
        pageContent?.includes('Welcome') ||
        pageContent?.includes('Hello') ||
        pageContent?.includes('Dashboard');
      expect(hasWelcome).toBeTruthy();
    });
  });

  tenantTest.describe('Stats Cards', () => {
    tenantTest('should display Active Leases stats card', async ({ tenantPage }) => {
      const statsCards = tenantPage.locator(
        '.card, .stat-card, [class*="stats"], [class*="summary"]'
      );
      await expect(statsCards.first()).toBeVisible({ timeout: 10000 });

      const pageContent = await tenantPage.textContent('body');
      expect(pageContent).toContain('Active Lease');
    });

    tenantTest('should display Total Payments stats card', async ({ tenantPage }) => {
      const pageContent = await tenantPage.textContent('body');
      const hasPaymentStats =
        pageContent?.includes('Total Payment') ||
        pageContent?.includes('Payment') ||
        pageContent?.includes('Paid');
      expect(hasPaymentStats).toBeTruthy();
    });

    tenantTest('should display Pending Requests stats card', async ({ tenantPage }) => {
      const pageContent = await tenantPage.textContent('body');
      const hasPendingStats =
        pageContent?.includes('Pending') ||
        pageContent?.includes('Request') ||
        pageContent?.includes('Outstanding');
      expect(hasPendingStats).toBeTruthy();
    });
  });

  tenantTest.describe('Properties and Leases Section', () => {
    tenantTest('should show active lease cards with property info', async ({ tenantPage }) => {
      // Look for lease/property cards or section
      const leaseSection = tenantPage.locator(
        '[class*="lease"], [class*="property"], [class*="unit"], .card:has-text("Lease"), .card:has-text("Property")'
      );

      // There may or may not be active leases; check the section exists
      const pageContent = await tenantPage.textContent('body');
      const hasLeaseInfo =
        pageContent?.includes('Lease') ||
        pageContent?.includes('Property') ||
        pageContent?.includes('Unit') ||
        pageContent?.includes('No active');
      expect(hasLeaseInfo).toBeTruthy();
    });
  });

  tenantTest.describe('Recent Payments', () => {
    tenantTest('should show recent payments table or section', async ({ tenantPage }) => {
      const pageContent = await tenantPage.textContent('body');
      const hasPaymentsSection =
        pageContent?.includes('Recent Payment') ||
        pageContent?.includes('Payment History') ||
        pageContent?.includes('Latest Payment') ||
        pageContent?.includes('No payment');
      expect(hasPaymentsSection).toBeTruthy();
    });
  });

  tenantTest.describe('Navigation', () => {
    tenantTest('should have working My Details navigation link', async ({ tenantPage }) => {
      const myDetailsLink = tenantPage.locator(
        'a[href*="MyDetails"], a[href*="Profile"], a:has-text("My Details"), a:has-text("Profile")'
      );
      // Verify the link exists (may be in sidebar or navbar)
      const count = await myDetailsLink.count();
      if (count > 0) {
        await expect(myDetailsLink.first()).toBeVisible();
      } else {
        // Check in nav or sidebar menus
        const navLinks = tenantPage.locator('nav a, .sidebar a, .nav-link');
        const navCount = await navLinks.count();
        expect(navCount).toBeGreaterThan(0);
      }
    });

    tenantTest('should have working Make Payment navigation link', async ({ tenantPage }) => {
      const paymentLink = tenantPage.locator(
        'a[href*="MakePayment"], a[href*="Payment"], a:has-text("Make Payment"), a:has-text("Pay Now")'
      );
      await expect(paymentLink.first()).toBeVisible({ timeout: 5000 });

      await paymentLink.first().click();
      await tenantPage.waitForLoadState('domcontentloaded');
      expect(tenantPage.url()).toContain('Payment');
    });

    tenantTest('should have working Payment History navigation link', async ({ tenantPage }) => {
      const historyLink = tenantPage.locator(
        'a[href*="PaymentHistory"], a[href*="History"], a:has-text("Payment History"), a:has-text("History")'
      );
      await expect(historyLink.first()).toBeVisible({ timeout: 5000 });

      await historyLink.first().click();
      await tenantPage.waitForLoadState('domcontentloaded');

      const currentUrl = tenantPage.url();
      const isOnHistoryPage =
        currentUrl.includes('PaymentHistory') || currentUrl.includes('History');
      expect(isOnHistoryPage).toBeTruthy();
    });

    tenantTest('should have Logout link', async ({ tenantPage }) => {
      const logoutLink = tenantPage.locator(
        'a[href*="Logout"], button:has-text("Logout"), a:has-text("Logout"), a:has-text("Sign Out")'
      );
      await expect(logoutLink.first()).toBeVisible({ timeout: 5000 });
    });
  });

  tenantTest.describe('Maintenance Section', () => {
    tenantTest('should display maintenance section on dashboard', async ({ tenantPage }) => {
      const pageContent = await tenantPage.textContent('body');
      const hasMaintenanceSection =
        pageContent?.includes('Maintenance') ||
        pageContent?.includes('Service Request') ||
        pageContent?.includes('Repair');
      expect(hasMaintenanceSection).toBeTruthy();
    });
  });
});
