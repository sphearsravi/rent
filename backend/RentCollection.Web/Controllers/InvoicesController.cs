using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Web.Controllers;

[Authorize(Roles = Roles.Financial)]
public class InvoicesController : Controller
{
    private readonly ApplicationDbContext _context;

    public InvoicesController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        try
        {
            var invoices = await _context.Invoices
                .Include(i => i.Lease)
                    .ThenInclude(l => l.Unit)
                        .ThenInclude(u => u.Property)
                .Include(i => i.Tenant)
                .OrderByDescending(i => i.InvoiceDate)
                .ToListAsync();

            return View(invoices);
        }
        catch (Exception)
        {
            return View(new List<Invoice>());
        }
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var invoice = await _context.Invoices
            .Include(i => i.Lease)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .Include(i => i.Tenant)
            .Include(i => i.Lease)
                .ThenInclude(l => l.Payments)
            .FirstOrDefaultAsync(m => m.InvoiceId == id);

        if (invoice == null)
        {
            return NotFound();
        }

        return View(invoice);
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var invoice = await _context.Invoices
            .Include(i => i.Lease)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .Include(i => i.Tenant)
            .FirstOrDefaultAsync(m => m.InvoiceId == id);

        if (invoice == null)
        {
            return NotFound();
        }

        return View(invoice);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var invoice = await _context.Invoices.FindAsync(id);
        if (invoice != null)
        {
            _context.Invoices.Remove(invoice);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Invoice deleted successfully!";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]   // Red-team #6: block CSRF-triggered invoice generation.
    public async Task<IActionResult> GenerateMonthlyInvoices()
    {
        try
        {
            var currentMonth = DateTime.UtcNow.Month;
            var currentYear = DateTime.UtcNow.Year;

            // Project only the scalar fields we need, with null-safe casts.
            // Materializing full Lease/Tenant/Unit entities threw
            // SqlNullValueException on demo rows that carry NULLs in
            // non-nullable columns — and since it's one SaveChanges, a single
            // bad row failed the WHOLE batch, so no invoices were generated.
            var activeLeases = await _context.Leases
                .Where(l => l.Status == "Active")
                .Select(l => new
                {
                    l.LeaseId,
                    TenantId = (int?)l.TenantId ?? 0,
                    MonthlyRent = (decimal?)l.MonthlyRent ?? 0m,
                    PaymentDueDay = (int?)l.PaymentDueDay ?? 1,
                    UnitNumber = l.Unit != null ? l.Unit.UnitNumber : null
                })
                .ToListAsync();

            var invoicesGenerated = 0;

            foreach (var lease in activeLeases)
            {
                // Check if a RENT invoice already exists for this month. Must
                // filter by InvoiceType — otherwise a Deposit/Insurance/Other
                // invoice raised this month (e.g. the deposit issued at lease
                // activation) makes this think rent is already billed and skips
                // it. This matches RecurringJobs.GenerateRentInvoicesAsync.
                var existingInvoice = await _context.Invoices
                    .AnyAsync(i => i.LeaseId == lease.LeaseId
                        && i.InvoiceType == "Rent"
                        && i.InvoiceDate.Month == currentMonth
                        && i.InvoiceDate.Year == currentYear);

                if (!existingInvoice)
                {
                    // LEASE_AUDIT Flaw #6 — clamp moved to LeaseMath so this
                    // controller and RecurringJobs use the exact same rule.
                    var dueDate = RentCollection.Core.Leases.LeaseMath.ComputeDueDate(
                        lease.PaymentDueDay, currentYear, currentMonth);

                    // Create invoice
                    var invoice = new Invoice
                    {
                        LeaseId = lease.LeaseId,
                        TenantId = lease.TenantId,
                        InvoiceDate = DateTime.UtcNow,
                        DueDate = dueDate,
                        TotalAmount = lease.MonthlyRent,
                        PaidAmount = 0,
                        Status = "Pending",
                        InvoiceType = "Rent",
                        Notes = $"Monthly rent for {DateTime.UtcNow:MMMM yyyy} - Unit {lease.UnitNumber}",
                        CreatedDate = DateTime.UtcNow
                    };

                    _context.Invoices.Add(invoice);
                    invoicesGenerated++;
                }
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Generated {invoicesGenerated} invoice(s) for {DateTime.UtcNow:MMMM yyyy}";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Error generating invoices: {ex.Message}";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    public async Task<IActionResult> MarkAsPaid(int id)
    {
        var invoice = await _context.Invoices.FindAsync(id);
        if (invoice != null)
        {
            invoice.PaidAmount = invoice.TotalAmount;
            invoice.Status = "Paid";
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Invoice marked as paid!";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> MarkAsOverdue(int id)
    {
        var invoice = await _context.Invoices.FindAsync(id);
        if (invoice != null)
        {
            invoice.Status = "Overdue";
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Invoice marked as overdue!";
        }

        return RedirectToAction(nameof(Index));
    }
}
