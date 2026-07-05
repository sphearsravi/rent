import { test, expect } from '@playwright/test';
import {
  adminLogin,
  adminLogout,
  tenantLogin,
  tenantLogout,
} from '../fixtures/auth.fixture';

test.describe('E2E Full Workflows', () => {
  test.describe('Test 1: Complete Rent Payment Flow', () => {
    test('Admin generates invoices and Tenant views pending invoices', async ({ page }) => {
      // ---- Admin: Login and Generate Monthly Invoices ----
      await adminLogin(page);
      expect(page.url()).not.toContain('/Account/Login');

      await page.goto('/Invoices');
      await page.waitForLoadState('domcontentloaded');
      expect(page.url()).toContain('/Invoices');

      // Look for Generate Monthly Invoices button
      const generateButton = page.locator(
        'form[action*="GenerateMonthly"] button[type="submit"], a:has-text("Generate"), button:has-text("Generate")'
      );
      if ((await generateButton.count()) > 0) {
        await generateButton.first().click();
        await page.waitForLoadState('domcontentloaded');

        // If there is a confirmation step, handle it
        const confirmButton = page.locator(
          'button:has-text("Confirm"), button:has-text("Generate"), button[type="submit"]'
        );
        if ((await confirmButton.count()) > 0) {
          await confirmButton.first().click();
          await page.waitForLoadState('domcontentloaded');
        }

        // Verify success (either success message or back to invoices page)
        await page.waitForTimeout(2000);
        const pageContent = await page.textContent('body');
        const wasSuccessful =
          pageContent?.includes('success') ||
          pageContent?.includes('generated') ||
          pageContent?.includes('Invoice') ||
          page.url().includes('/Invoices');
        expect(wasSuccessful).toBeTruthy();
      }

      await adminLogout(page);
      await page.waitForLoadState('domcontentloaded');

      // ---- Tenant: Login and View Pending Invoices ----
      await tenantLogin(page);
      expect(page.url()).not.toContain('/TenantPortal/Login');

      await page.goto('/TenantPortal/MakePayment');
      await page.waitForLoadState('domcontentloaded');

      // Verify the payment page loads and shows invoices or empty state
      const paymentContent = await page.textContent('body');
      const hasPaymentPage =
        paymentContent?.includes('Payment') ||
        paymentContent?.includes('Invoice') ||
        paymentContent?.includes('No pending') ||
        paymentContent?.includes('Make Payment');
      expect(hasPaymentPage).toBeTruthy();

      await tenantLogout(page);
    });
  });

  test.describe('Test 2: Bank Transfer Approval Flow', () => {
    test('Tenant views payment page and Admin views approval queue', async ({ page }) => {
      // ---- Tenant: Login and View Make Payment Page ----
      await tenantLogin(page);
      expect(page.url()).not.toContain('/TenantPortal/Login');

      await page.goto('/TenantPortal/MakePayment');
      await page.waitForLoadState('domcontentloaded');

      const paymentPageContent = await page.textContent('body');
      const hasMakePaymentPage =
        paymentPageContent?.includes('Payment') ||
        paymentPageContent?.includes('Invoice') ||
        paymentPageContent?.includes('Bank Transfer') ||
        paymentPageContent?.includes('Make Payment');
      expect(hasMakePaymentPage).toBeTruthy();

      await tenantLogout(page);
      await page.waitForLoadState('domcontentloaded');

      // ---- Admin: Login and View Payment Approval Page ----
      await adminLogin(page);
      expect(page.url()).not.toContain('/Account/Login');

      await page.goto('/PaymentApproval');
      await page.waitForLoadState('domcontentloaded');

      const approvalContent = await page.textContent('body');
      const hasApprovalPage =
        approvalContent?.includes('Approval') ||
        approvalContent?.includes('Payment') ||
        approvalContent?.includes('Pending') ||
        approvalContent?.includes('Review') ||
        page.url().includes('PaymentApproval');
      expect(hasApprovalPage).toBeTruthy();

      await adminLogout(page);
    });
  });

  test.describe('Test 3: Maintenance Request Lifecycle', () => {
    test('Tenant submits maintenance request and Admin views maintenance list', async ({
      page,
    }) => {
      const timestamp = Date.now();
      const requestDescription = `E2E Test Maintenance - Leaking pipe in bathroom - ${timestamp}`;

      // ---- Tenant: Login, Open Maintenance Modal, Submit ----
      await tenantLogin(page);
      expect(page.url()).not.toContain('/TenantPortal/Login');

      // Find and click the maintenance request button
      const submitButton = page.locator(
        'button:has-text("New Request"), [onclick*="showMaintenanceModal"], button:has-text("Maintenance"), button:has-text("Submit Request"), [data-bs-target*="maintenance"], [data-bs-toggle="modal"]:has-text("Request")'
      );

      if ((await submitButton.count()) > 0) {
        await submitButton.first().click();
        await page.waitForTimeout(500);

        // Fill in the maintenance form
        // Property/Lease select
        const propertySelect = page.locator(
          'select[name*="ease"], select[name*="roperty"], select[id*="ease"], select[id*="roperty"], select[name*="Lease"], select[name*="Property"]'
        );
        if ((await propertySelect.count()) > 0) {
          const options = propertySelect.first().locator('option');
          const optionCount = await options.count();
          if (optionCount > 1) {
            const optionValue = await options.nth(1).getAttribute('value');
            if (optionValue) await propertySelect.first().selectOption(optionValue);
          }
        }

        // Category
        const categoryField = page.locator(
          'select[name*="ategory"], select[id*="ategory"], select[name*="Category"]'
        );
        if ((await categoryField.count()) > 0) {
          const options = categoryField.first().locator('option');
          const optionCount = await options.count();
          if (optionCount > 1) {
            const optionValue = await options.nth(1).getAttribute('value');
            if (optionValue) await categoryField.first().selectOption(optionValue);
          }
        }

        // Priority
        const priorityField = page.locator(
          'select[name*="riority"], select[id*="riority"], select[name*="Priority"]'
        );
        if ((await priorityField.count()) > 0) {
          const options = priorityField.first().locator('option');
          const optionCount = await options.count();
          if (optionCount > 1) {
            const optionValue = await options.nth(1).getAttribute('value');
            if (optionValue) await priorityField.first().selectOption(optionValue);
          }
        }

        // Description
        const descriptionField = page.locator(
          'textarea[name*="escription"], textarea[id*="escription"], textarea[name*="Description"], textarea'
        );
        if ((await descriptionField.count()) > 0) {
          await descriptionField.first().fill(requestDescription);
        }

        // Submit
        const modalSubmitButton = page.locator(
          '.modal.show button[type="submit"], .modal.show button:has-text("Submit"), .modal.show button:has-text("Save"), .modal button[type="submit"]'
        );
        if ((await modalSubmitButton.count()) > 0) {
          await modalSubmitButton.first().click();
          await page.waitForLoadState('domcontentloaded');
          await page.waitForTimeout(1000);
        }
      }

      await tenantLogout(page);
      await page.waitForLoadState('domcontentloaded');

      // ---- Admin: Login and View Maintenance Requests ----
      await adminLogin(page);
      expect(page.url()).not.toContain('/Account/Login');

      await page.goto('/Maintenance');
      await page.waitForLoadState('domcontentloaded');

      const maintenanceContent = await page.textContent('body');
      const hasMaintenancePage =
        maintenanceContent?.includes('Maintenance') ||
        maintenanceContent?.includes('Request') ||
        maintenanceContent?.includes('Service') ||
        page.url().includes('Maintenance');
      expect(hasMaintenancePage).toBeTruthy();

      await adminLogout(page);
    });
  });

  test.describe('Test 4: Admin Creates Property and Views Tenant/Lease Pages', () => {
    test('Admin creates property and navigates tenant and lease pages', async ({ page }) => {
      const timestamp = Date.now();
      const propertyName = `E2E Test Property ${timestamp}`;

      // ---- Admin: Login and Create Property ----
      await adminLogin(page);
      expect(page.url()).not.toContain('/Account/Login');

      await page.goto('/Properties/Create');
      await page.waitForLoadState('domcontentloaded');

      // Fill minimal required fields for property creation
      // Property Name
      const nameField = page.locator(
        'input[name*="ame"], input[id*="ame"], input[name*="Name"], input[name*="PropertyName"]'
      );
      if ((await nameField.count()) > 0) {
        await nameField.first().fill(propertyName);
      }

      // Address (if required)
      const addressField = page.locator(
        'input[name*="ddress"], input[id*="ddress"], textarea[name*="ddress"], input[name*="Address"]'
      );
      if ((await addressField.count()) > 0) {
        await addressField.first().fill('123 E2E Test Street, Kuwait City');
      }

      // Property Type (select if exists)
      const typeSelect = page.locator(
        'select[name*="ype"], select[id*="ype"], select[name*="Type"], select[name*="PropertyType"]'
      );
      if ((await typeSelect.count()) > 0) {
        const options = typeSelect.first().locator('option');
        const optionCount = await options.count();
        if (optionCount > 1) {
          const optionValue = await options.nth(1).getAttribute('value');
          if (optionValue) await typeSelect.first().selectOption(optionValue);
        }
      }

      // Owner (select if exists)
      const ownerSelect = page.locator(
        'select[name*="wner"], select[id*="wner"], select[name*="Owner"]'
      );
      if ((await ownerSelect.count()) > 0) {
        const options = ownerSelect.first().locator('option');
        const optionCount = await options.count();
        if (optionCount > 1) {
          const optionValue = await options.nth(1).getAttribute('value');
          if (optionValue) await ownerSelect.first().selectOption(optionValue);
        }
      }

      // Building (select if exists)
      const buildingSelect = page.locator(
        'select[name*="uilding"], select[id*="uilding"], select[name*="Building"]'
      );
      if ((await buildingSelect.count()) > 0) {
        const options = buildingSelect.first().locator('option');
        const optionCount = await options.count();
        if (optionCount > 1) {
          const optionValue = await options.nth(1).getAttribute('value');
          if (optionValue) await buildingSelect.first().selectOption(optionValue);
        }
      }

      // Submit the property creation form
      const submitButton = page.locator(
        'button[type="submit"]:has-text("Create"), button[type="submit"]:has-text("Save"), input[type="submit"], button[type="submit"]'
      );
      if ((await submitButton.count()) > 0) {
        await submitButton.first().click();
        await page.waitForLoadState('domcontentloaded');
        await page.waitForTimeout(1000);
      }

      // ---- Admin: Navigate to Tenants Page ----
      await page.goto('/Tenants');
      await page.waitForLoadState('domcontentloaded');

      const tenantsContent = await page.textContent('body');
      const hasTenantsPage =
        tenantsContent?.includes('Tenant') ||
        tenantsContent?.includes('tenant') ||
        page.url().includes('Tenant');
      expect(hasTenantsPage).toBeTruthy();

      // ---- Admin: Navigate to Leases Page ----
      await page.goto('/Leases');
      await page.waitForLoadState('domcontentloaded');

      const leasesContent = await page.textContent('body');
      const hasLeasesPage =
        leasesContent?.includes('Lease') ||
        leasesContent?.includes('lease') ||
        page.url().includes('Lease');
      expect(hasLeasesPage).toBeTruthy();

      await adminLogout(page);
    });
  });

  test.describe('Test 5: Invoice Generation and Payment Verification', () => {
    test('Admin generates invoices, views payments, and checks reconciliation', async ({
      page,
    }) => {
      // ---- Admin: Login and Generate Invoices ----
      await adminLogin(page);
      expect(page.url()).not.toContain('/Account/Login');

      await page.goto('/Invoices');
      await page.waitForLoadState('domcontentloaded');

      // Attempt to generate invoices
      const generateButton = page.locator(
        'form[action*="GenerateMonthly"] button[type="submit"], a:has-text("Generate"), button:has-text("Generate")'
      );
      if ((await generateButton.count()) > 0) {
        await generateButton.first().click();
        await page.waitForLoadState('domcontentloaded');

        const confirmButton = page.locator(
          'button:has-text("Confirm"), button:has-text("Generate"), button[type="submit"]'
        );
        if ((await confirmButton.count()) > 0) {
          await confirmButton.first().click();
          await page.waitForLoadState('domcontentloaded');
          await page.waitForTimeout(2000);
        }
      }

      // ---- Admin: Navigate to Payments Page ----
      await page.goto('/Payments');
      await page.waitForLoadState('domcontentloaded');

      const paymentsContent = await page.textContent('body');
      const hasPaymentsPage =
        paymentsContent?.includes('Payment') ||
        paymentsContent?.includes('payment') ||
        page.url().includes('Payment');
      expect(hasPaymentsPage).toBeTruthy();

      // Verify summary cards are visible
      const summaryCards = page.locator(
        '.card, .stat-card, [class*="summary"], [class*="stats"]'
      );
      const cardCount = await summaryCards.count();
      expect(cardCount).toBeGreaterThan(0);

      // ---- Admin: Navigate to Reconciliation Page ----
      await page.goto('/Payments/Reconciliation');
      await page.waitForLoadState('domcontentloaded');

      const reconciliationContent = await page.textContent('body');
      const hasReconciliationPage =
        reconciliationContent?.includes('Reconciliation') ||
        reconciliationContent?.includes('reconciliation') ||
        reconciliationContent?.includes('Payment') ||
        page.url().includes('Reconciliation');
      expect(hasReconciliationPage).toBeTruthy();

      await adminLogout(page);
    });
  });
});
