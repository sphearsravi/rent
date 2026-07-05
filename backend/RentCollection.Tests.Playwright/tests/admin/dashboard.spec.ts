import { expect } from '@playwright/test';
import { adminTest } from '../fixtures/auth.fixture';

adminTest.describe('Admin Dashboard', () => {
  adminTest.beforeEach(async ({ adminPage }) => {
    await adminPage.goto('/Dashboard');
    await adminPage.waitForLoadState('load');
  });

  adminTest.describe('Stat Cards', () => {
    adminTest('should display 6 dashboard stat cards', async ({ adminPage }) => {
      // Wait for stat cards to load (actual class: .stat-card)
      await adminPage.waitForSelector('.stat-card', { timeout: 10000 });

      // Verify key metrics are present by looking for their exact text
      const expectedLabels = [
        'Total Properties',
        'Occupied Units',
        'Active Tenants',
        'Revenue (This Month)',
        'Pending Payments',
        'Open Maintenance',
      ];

      for (const label of expectedLabels) {
        const element = adminPage.locator(`text=${label}`).first();
        await expect(element).toBeVisible({ timeout: 5000 });
      }
    });
  });

  adminTest.describe('Recent Payments Table', () => {
    adminTest('should display recent payments section', async ({ adminPage }) => {
      const recentPayments = adminPage.locator('h2:has-text("Recent Payments")');
      await expect(recentPayments).toBeVisible({ timeout: 10000 });

      // There should be a table for payments
      const paymentTable = adminPage.locator('.modern-table').first();
      await expect(paymentTable).toBeVisible({ timeout: 5000 });
    });
  });

  adminTest.describe('Lease Expirations', () => {
    adminTest('should display upcoming lease expirations section', async ({ adminPage }) => {
      const leaseSection = adminPage.locator('h2:has-text("Upcoming Lease Expirations")');
      await expect(leaseSection).toBeVisible({ timeout: 10000 });
    });
  });

  adminTest.describe('Quick Action Buttons', () => {
    const quickActions = [
      { label: 'Add Property', expectedUrl: '/Properties/Create' },
      { label: 'Add Tenant', expectedUrl: '/Tenants/Create' },
      { label: 'Record Payment', expectedUrl: '/Payments/Create' },
      { label: 'New Maintenance', expectedUrl: '/Maintenance/Create' },
      { label: 'View Reports', expectedUrl: '/Reports' },
    ];

    for (const action of quickActions) {
      adminTest(
        `should navigate to correct page when clicking "${action.label}"`,
        async ({ adminPage }) => {
          const button = adminPage
            .locator(`a.action-btn:has-text("${action.label}")`)
            .first();
          await expect(button).toBeVisible({ timeout: 10000 });

          await button.click();
          await adminPage.waitForLoadState('load');

          expect(adminPage.url()).toContain(action.expectedUrl);
        }
      );
    }
  });

  adminTest.describe('Sidebar Navigation', () => {
    const sidebarItems = [
      { label: 'Dashboard', expectedUrl: '/Dashboard' },
      { label: 'Properties', expectedUrl: '/Properties' },
      { label: 'Tenants', expectedUrl: '/Tenants' },
      { label: 'Leases', expectedUrl: '/Leases' },
      { label: 'Payments', expectedUrl: '/Payments' },
      { label: 'Invoices', expectedUrl: '/Invoices' },
      { label: 'Reports', expectedUrl: '/Reports' },
      { label: 'Approvals', expectedUrl: '/PaymentApproval' },
      { label: 'Maintenance', expectedUrl: '/Maintenance' },
      { label: 'Users', expectedUrl: '/Users' },
    ];

    for (const item of sidebarItems) {
      adminTest(
        `should navigate to ${item.label} page from sidebar`,
        async ({ adminPage }) => {
          // Sidebar links use .nav-item class
          const sidebarLink = adminPage
            .locator(
              `a.nav-item:has-text("${item.label}")`
            )
            .first();
          await expect(sidebarLink).toBeVisible({ timeout: 10000 });

          await sidebarLink.click();
          await adminPage.waitForLoadState('load');

          expect(adminPage.url()).toContain(item.expectedUrl);
        }
      );
    }
  });
});
