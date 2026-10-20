using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Web.ViewComponents;

/// <summary>One actionable admin alert shown in the notification-bell dropdown.</summary>
public record BellAlert(string Icon, string Label, int Count, string Href, string Color);

/// <summary>
/// Replaces the old static bell (which showed a hardcoded "3" and did nothing).
/// Computes a live count of things that need attention and renders a real
/// dropdown that links to the relevant pages.
/// </summary>
public class NotificationBellViewComponent : ViewComponent
{
    private readonly ApplicationDbContext _db;
    public NotificationBellViewComponent(ApplicationDbContext db) => _db = db;

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var today = DateTime.Today;
        var alerts = new List<BellAlert>();

        // Overdue = the Invoices page's own definition (Status == "Overdue") so the
        // badge count matches the "Overdue" tab it links to.
        var overdue = await _db.Invoices.CountAsync(i => i.Status == "Overdue");
        if (overdue > 0)
            alerts.Add(new("fa-file-invoice-dollar", "Overdue invoices", overdue, "/Invoices?status=Overdue", "#dc2626"));

        var maintenance = await _db.MaintenanceRequests.CountAsync(m =>
            m.Status != "Completed" && m.Status != "Closed" && m.Status != "Cancelled");
        if (maintenance > 0)
            alerts.Add(new("fa-screwdriver-wrench", "Open maintenance requests", maintenance, "/Maintenance?status=open", "#d97706"));

        var expiring = await _db.Leases.CountAsync(l =>
            l.Status == "Active" && l.LeaseEndDate >= today && l.LeaseEndDate <= today.AddDays(30));
        if (expiring > 0)
            alerts.Add(new("fa-hourglass-half", "Leases expiring soon", expiring, "/Leases?expiring=30", "#d97706"));

        var openCases = await _db.LegalCases.CountAsync(c => c.Status == "Open" || c.Status == "Hearing");
        if (openCases > 0)
            alerts.Add(new("fa-scale-balanced", "Open legal cases", openCases, "/Leases?case=__open", "#b45309"));

        return View(alerts);
    }
}
