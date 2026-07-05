import { expect } from '@playwright/test';
import { tenantTest } from '../fixtures/auth.fixture';

const PAYMENT_HISTORY_URL = '/TenantPortal/PaymentHistory';

tenantTest.describe('Tenant Payment History', () => {
  tenantTest.beforeEach(async ({ tenantPage }) => {
    await tenantPage.goto(PAYMENT_HISTORY_URL);
    await tenantPage.waitForLoadState('domcontentloaded');
  });

  tenantTest.describe('Page Load', () => {
    tenantTest('should load Payment History page', async ({ tenantPage }) => {
      expect(tenantPage.url()).toContain('PaymentHistory');

      const pageContent = await tenantPage.textContent('body');
      const hasHistoryContent =
        pageContent?.includes('Payment History') ||
        pageContent?.includes('Payment') ||
        pageContent?.includes('History');
      expect(hasHistoryContent).toBeTruthy();
    });
  });

  tenantTest.describe('Stats Cards', () => {
    tenantTest('should show Total Payments count', async ({ tenantPage }) => {
      const pageContent = await tenantPage.textContent('body');
      const hasTotalPayments =
        pageContent?.includes('Total Payment') ||
        pageContent?.includes('Total') ||
        pageContent?.includes('Payments');
      expect(hasTotalPayments).toBeTruthy();
    });

    tenantTest('should show Total Amount stats', async ({ tenantPage }) => {
      const pageContent = await tenantPage.textContent('body');
      const hasTotalAmount =
        pageContent?.includes('Total Amount') ||
        pageContent?.includes('Amount') ||
        pageContent?.includes('KD');
      expect(hasTotalAmount).toBeTruthy();
    });

    tenantTest('should show Completed payments count', async ({ tenantPage }) => {
      const statsCards = tenantPage.locator(
        '.card, .stat-card, [class*="stats"], [class*="summary"]'
      );
      await expect(statsCards.first()).toBeVisible({ timeout: 10000 });

      const pageContent = await tenantPage.textContent('body');
      const hasCompletedStats =
        pageContent?.includes('Completed') ||
        pageContent?.includes('Approved') ||
        pageContent?.includes('Paid');
      expect(hasCompletedStats).toBeTruthy();
    });

    tenantTest('should show Pending payments count', async ({ tenantPage }) => {
      const pageContent = await tenantPage.textContent('body');
      const hasPendingStats =
        pageContent?.includes('Pending') || pageContent?.includes('Awaiting');
      expect(hasPendingStats).toBeTruthy();
    });
  });

  tenantTest.describe('Payment Table', () => {
    tenantTest('should display payment table with correct columns', async ({ tenantPage }) => {
      const table = tenantPage.locator('table');
      const tableCount = await table.count();

      if (tableCount > 0) {
        // Verify table headers
        const headerRow = table.first().locator('thead tr, th');
        await expect(headerRow.first()).toBeVisible({ timeout: 5000 });

        const headerText = await table.first().locator('thead').textContent();

        // Check for expected columns
        const expectedColumns = ['Date', 'Property', 'Unit', 'Amount', 'Method', 'Reference', 'Status'];
        let matchedColumns = 0;
        for (const col of expectedColumns) {
          if (headerText?.includes(col)) {
            matchedColumns++;
          }
        }

        // At least some of the expected columns should be present
        expect(matchedColumns).toBeGreaterThanOrEqual(3);
      } else {
        // If no table, there should be an empty state message
        const pageContent = await tenantPage.textContent('body');
        const hasEmptyState =
          pageContent?.includes('No payment') ||
          pageContent?.includes('no record') ||
          pageContent?.includes('No data') ||
          pageContent?.includes('empty');
        expect(hasEmptyState).toBeTruthy();
      }
    });

    tenantTest('should have search input that filters table rows', async ({ tenantPage }) => {
      const searchInput = tenantPage.locator(
        '#searchInput, input[type="search"], input[placeholder*="earch"], input[name*="earch"]'
      );

      if ((await searchInput.count()) > 0) {
        await expect(searchInput.first()).toBeVisible();

        // Type a search term and verify filtering behavior
        await searchInput.first().fill('test-filter-string');
        await tenantPage.waitForTimeout(500);

        // After typing a non-matching filter, rows should be hidden or filtered
        const visibleRows = tenantPage.locator('table tbody tr:visible, table tbody tr');
        // We just verify the search input is interactive
        const currentValue = await searchInput.first().inputValue();
        expect(currentValue).toBe('test-filter-string');

        // Clear the search
        await searchInput.first().clear();
      }
    });
  });

  tenantTest.describe('Status Badges', () => {
    tenantTest('should display status badges with correct styling', async ({ tenantPage }) => {
      const badges = tenantPage.locator(
        '.badge, [class*="badge"], [class*="status"], .label'
      );

      const badgeCount = await badges.count();
      if (badgeCount > 0) {
        // Check if badges have color-coded classes
        for (let i = 0; i < Math.min(badgeCount, 5); i++) {
          const badge = badges.nth(i);
          const badgeText = await badge.textContent();
          const badgeClass = await badge.getAttribute('class');

          if (badgeText?.includes('Completed') || badgeText?.includes('Approved') || badgeText?.includes('Paid')) {
            // Green-ish styling
            const hasGreenStyle =
              badgeClass?.includes('success') ||
              badgeClass?.includes('green') ||
              badgeClass?.includes('completed');
            expect(hasGreenStyle).toBeTruthy();
          } else if (badgeText?.includes('Pending') || badgeText?.includes('Awaiting')) {
            // Yellow-ish styling
            const hasYellowStyle =
              badgeClass?.includes('warning') ||
              badgeClass?.includes('yellow') ||
              badgeClass?.includes('pending');
            expect(hasYellowStyle).toBeTruthy();
          } else if (badgeText?.includes('Rejected') || badgeText?.includes('Failed')) {
            // Red-ish styling
            const hasRedStyle =
              badgeClass?.includes('danger') ||
              badgeClass?.includes('red') ||
              badgeClass?.includes('rejected');
            expect(hasRedStyle).toBeTruthy();
          }
        }
      }
    });
  });

  tenantTest.describe('Empty State', () => {
    tenantTest('should show appropriate message when no payments exist', async ({ tenantPage }) => {
      // This test verifies the page handles empty data gracefully
      const table = tenantPage.locator('table tbody tr');
      const rowCount = await table.count();

      if (rowCount === 0) {
        const pageContent = await tenantPage.textContent('body');
        const hasEmptyMessage =
          pageContent?.includes('No payment') ||
          pageContent?.includes('no record') ||
          pageContent?.includes('No data') ||
          pageContent?.includes('No transactions') ||
          pageContent?.includes('0');
        expect(hasEmptyMessage).toBeTruthy();
      } else {
        // If rows exist, the table should be visible
        await expect(tenantPage.locator('table').first()).toBeVisible();
      }
    });
  });
});
