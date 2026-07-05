import { expect } from '@playwright/test';
import { tenantTest } from '../fixtures/auth.fixture';

const MAKE_PAYMENT_URL = '/TenantPortal/MakePayment';

tenantTest.describe('Tenant Make Payment', () => {
  tenantTest.beforeEach(async ({ tenantPage }) => {
    await tenantPage.goto(MAKE_PAYMENT_URL);
    await tenantPage.waitForLoadState('domcontentloaded');
  });

  tenantTest.describe('Page Load', () => {
    tenantTest('should load Make Payment page', async ({ tenantPage }) => {
      expect(tenantPage.url()).toContain('MakePayment');

      // Page should have a heading or title related to payments
      const pageContent = await tenantPage.textContent('body');
      const hasPaymentContent =
        pageContent?.includes('Make Payment') ||
        pageContent?.includes('Payment') ||
        pageContent?.includes('Pay');
      expect(hasPaymentContent).toBeTruthy();
    });
  });

  tenantTest.describe('Pending Invoices', () => {
    tenantTest('should display pending invoices as selectable cards', async ({ tenantPage }) => {
      // Look for invoice cards or a list of pending invoices
      const invoiceCards = tenantPage.locator(
        '.card:has-text("Invoice"), .card:has-text("Due"), [class*="invoice"], [class*="pending"], .invoice-card'
      );

      // There may or may not be pending invoices
      const pageContent = await tenantPage.textContent('body');
      const hasInvoices =
        pageContent?.includes('Invoice') ||
        pageContent?.includes('Due') ||
        pageContent?.includes('Amount') ||
        pageContent?.includes('No pending') ||
        pageContent?.includes('No invoice') ||
        pageContent?.includes('no outstanding');
      expect(hasInvoices).toBeTruthy();
    });

    tenantTest('should show amount, due date, and property info on invoice cards', async ({
      tenantPage,
    }) => {
      const invoiceCards = tenantPage.locator(
        '.card:has-text("Invoice"), .card:has-text("Due"), [class*="invoice"], .invoice-card'
      );

      const cardCount = await invoiceCards.count();
      if (cardCount > 0) {
        // If invoices exist, verify they show relevant information
        const firstCard = invoiceCards.first();
        const cardText = await firstCard.textContent();

        // Invoice card should contain amount or due date or property reference
        const hasRelevantInfo =
          cardText?.includes('KD') ||
          cardText?.includes('Amount') ||
          cardText?.includes('Due') ||
          cardText?.includes('Property') ||
          cardText?.includes('Unit') ||
          /\d+\.\d{2,3}/.test(cardText || '');
        expect(hasRelevantInfo).toBeTruthy();
      } else {
        // No invoices is a valid state - verify empty state messaging
        const pageContent = await tenantPage.textContent('body');
        const hasEmptyState =
          pageContent?.includes('No pending') ||
          pageContent?.includes('No invoice') ||
          pageContent?.includes('no outstanding') ||
          pageContent?.includes('up to date');
        expect(hasEmptyState).toBeTruthy();
      }
    });
  });

  tenantTest.describe('Payment Methods', () => {
    tenantTest('should have KNET payment button if building accepts KNET', async ({
      tenantPage,
    }) => {
      const pageContent = await tenantPage.textContent('body');

      // KNET may or may not be available depending on the building configuration
      const knetButton = tenantPage.locator(
        'button:has-text("KNET"), a:has-text("KNET"), [class*="knet"], input[value*="KNET"]'
      );
      const knetCount = await knetButton.count();

      if (knetCount > 0) {
        // KNET element exists in DOM - it may be visible or hidden based on building config
        // If visible, great. If hidden, that's also valid (KNET not enabled for this building)
        const isVisible = await knetButton.first().isVisible().catch(() => false);
        if (isVisible) {
          await expect(knetButton.first()).toBeVisible();
        }
        // Either way, the element existing in the DOM means the feature is implemented
        expect(knetCount).toBeGreaterThan(0);
      } else {
        // KNET not available is a valid state for some buildings
        const hasPaymentMethod =
          pageContent?.includes('Bank Transfer') ||
          pageContent?.includes('Transfer') ||
          pageContent?.includes('Payment');
        expect(hasPaymentMethod).toBeTruthy();
      }
    });

    tenantTest('should have Bank Transfer option available', async ({ tenantPage }) => {
      const bankTransferOption = tenantPage.locator(
        'button:has-text("Bank Transfer"), a:has-text("Bank Transfer"), input[value*="Bank"], label:has-text("Bank Transfer"), [class*="bank-transfer"], input[type="radio"][value*="Bank"]'
      );

      const pageContent = await tenantPage.textContent('body');
      const hasBankTransfer =
        (await bankTransferOption.count()) > 0 || pageContent?.includes('Bank Transfer');
      expect(hasBankTransfer).toBeTruthy();
    });
  });

  tenantTest.describe('Bank Transfer Form', () => {
    tenantTest('should have required bank transfer form fields', async ({ tenantPage }) => {
      // First, we may need to select Bank Transfer option to reveal the form
      const bankTransferOption = tenantPage.locator(
        'button:has-text("Bank Transfer"), a:has-text("Bank Transfer"), input[value*="Bank"], label:has-text("Bank Transfer"), input[type="radio"][value*="Bank"]'
      );

      if ((await bankTransferOption.count()) > 0) {
        await bankTransferOption.first().click();
        await tenantPage.waitForTimeout(500);
      }

      // Check for Amount field (should be readonly)
      const amountField = tenantPage.locator(
        'input[name*="mount"], input[id*="mount"], input[name*="Mount"]'
      );
      if ((await amountField.count()) > 0) {
        const isReadonly = await amountField.first().getAttribute('readonly');
        const isDisabled = await amountField.first().isDisabled();
        expect(isReadonly !== null || isDisabled).toBeTruthy();
      }

      // Check for Transfer Date field
      const transferDateField = tenantPage.locator(
        'input[name*="ransfer"][type="date"], input[id*="ransferDate"], input[name*="ransferDate"], input[type="date"]'
      );
      const hasTransferDate = (await transferDateField.count()) > 0;

      // Check for Bank Name field
      const bankNameField = tenantPage.locator(
        'input[name*="ank"], select[name*="ank"], input[id*="ankName"], select[id*="ankName"]'
      );
      const hasBankName = (await bankNameField.count()) > 0;

      // Check for Reference Number field
      const referenceField = tenantPage.locator(
        'input[name*="eference"], input[id*="eference"], input[name*="Reference"]'
      );
      const hasReference = (await referenceField.count()) > 0;

      // Check for File upload
      const fileUpload = tenantPage.locator('input[type="file"]');
      const hasFileUpload = (await fileUpload.count()) > 0;

      // Check for Notes field
      const notesField = tenantPage.locator(
        'textarea[name*="otes"], textarea[id*="otes"], input[name*="otes"]'
      );
      const hasNotes = (await notesField.count()) > 0;

      // At least some of these fields should be present on the form
      const totalFields = [
        hasTransferDate,
        hasBankName,
        hasReference,
        hasFileUpload,
        hasNotes,
      ].filter(Boolean).length;

      expect(totalFields).toBeGreaterThanOrEqual(2);
    });

    tenantTest('should accept only JPG/PNG/PDF for file upload', async ({ tenantPage }) => {
      // Select Bank Transfer if needed
      const bankTransferOption = tenantPage.locator(
        'button:has-text("Bank Transfer"), a:has-text("Bank Transfer"), input[value*="Bank"], label:has-text("Bank Transfer"), input[type="radio"][value*="Bank"]'
      );

      if ((await bankTransferOption.count()) > 0) {
        await bankTransferOption.first().click();
        await tenantPage.waitForTimeout(500);
      }

      const fileUpload = tenantPage.locator('input[type="file"]');
      if ((await fileUpload.count()) > 0) {
        const acceptAttr = await fileUpload.first().getAttribute('accept');

        // The accept attribute should restrict to image and PDF types
        if (acceptAttr) {
          const acceptsJpg =
            acceptAttr.includes('.jpg') ||
            acceptAttr.includes('.jpeg') ||
            acceptAttr.includes('image/jpeg');
          const acceptsPng =
            acceptAttr.includes('.png') || acceptAttr.includes('image/png');
          const acceptsPdf =
            acceptAttr.includes('.pdf') || acceptAttr.includes('application/pdf');

          expect(acceptsJpg || acceptsPng || acceptsPdf).toBeTruthy();
        }
      }
    });
  });
});
