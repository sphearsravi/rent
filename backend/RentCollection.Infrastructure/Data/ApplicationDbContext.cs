using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;

namespace RentCollection.Infrastructure.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly ICompanyContext? _companyContext;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Constructor used at request time. Migrations / design-time tools / unit
    /// tests use the parameterless overload above — the company filter then
    /// silently yields all rows (CurrentCompanyId is null, CurrentIsSuperAdmin
    /// is true, both bypass the predicate).
    /// </summary>
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ICompanyContext companyContext)
        : base(options)
    {
        _companyContext = companyContext;
    }

    /// <summary>
    /// Active company scope for query filters. Property access (rather than a
    /// captured local) is the EF-recommended pattern — EF parameterises this
    /// at query time and re-reads on every request.
    /// </summary>
    public int? CurrentCompanyId => _companyContext?.CompanyId;

    /// <summary>
    /// When true the company filter is bypassed. Defaults to true when no
    /// context is wired (migrations, tests, background jobs).
    /// </summary>
    public bool BypassCompanyFilter => _companyContext == null || _companyContext.IsSuperAdmin;

    public DbSet<User> Users { get; set; }
    public DbSet<Company> Companies { get; set; }
    public DbSet<Property> Properties { get; set; }
    public DbSet<Unit> Units { get; set; }
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<Lease> Leases { get; set; }
    public DbSet<Payment> Payments { get; set; }
    public DbSet<CashPayment> CashPayments { get; set; }
    public DbSet<BankTransfer> BankTransfers { get; set; }
    public DbSet<MaintenanceRequest> MaintenanceRequests { get; set; }
    public DbSet<Invoice> Invoices { get; set; }
    public DbSet<Document> Documents { get; set; }
    public DbSet<DocumentLink> DocumentLinks { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;
    public DbSet<ClosedPeriod> ClosedPeriods { get; set; } = null!;

    // New entities for multi-building KNET support
    public DbSet<PaymentAudit> PaymentAudits { get; set; }
    public DbSet<PaymentIdempotency> PaymentIdempotencies { get; set; }
    public DbSet<KNETReconciliation> KNETReconciliations { get; set; }

    // Notifications outbox (Item 1 of post-audit roadmap)
    public DbSet<Notification> Notifications { get; set; } = null!;

    // Sprint 25 — singleton SMTP config so admins can change provider
    // without re-deploying. See EmailSettings entity.
    public DbSet<EmailSettings> EmailSettings { get; set; } = null!;

    // Sprint 26 — FCM device tokens per tenant. One row per (tenant, install).
    public DbSet<TenantDeviceToken> TenantDeviceTokens { get; set; } = null!;
    // Refunds / credit notes (Item 8)
    public DbSet<Refund> Refunds { get; set; } = null!;
    // Maintenance photos + vendors (Item 7)
    public DbSet<Vendor> Vendors { get; set; } = null!;
    public DbSet<MaintenancePhoto> MaintenancePhotos { get; set; } = null!;

    /// <summary>
    /// Silences the
    /// <c>PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning</c>
    /// noise we saw in every startup. The warning flags entities (Unit /
    /// Payment / Invoice / BankTransfer / CashPayment / KNETReconciliation /
    /// MaintenanceRequest / Lease) with required navigations to a parent that
    /// has a company-scope query filter. In our data model the matching scope
    /// is enforced through other paths (every dependent shares CompanyId via
    /// its parent's lifecycle), so the warning is a false positive here.
    /// Adding a symmetric HasQueryFilter on each dependent would require a
    /// CompanyId column on each — tracked for Sprint 4 (centralized
    /// multi-tenancy convention).
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w => w.Ignore(
            CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Company-scope global query filters. When a request-time DbContext is
        // used (i.e. _companyContext != null) and the caller is NOT a SuperAdmin,
        // these filters silently restrict rows to the caller's CompanyId.
        // Migrations and design-time tooling pass through with no filter.
        modelBuilder.Entity<Property>().HasQueryFilter(p =>
            BypassCompanyFilter || p.CompanyId == CurrentCompanyId);

        // Tenant — also soft-delete aware (Item 10).
        modelBuilder.Entity<Tenant>().HasQueryFilter(t =>
            !t.IsDeleted && (BypassCompanyFilter || t.CompanyId == CurrentCompanyId));

        modelBuilder.Entity<User>().HasQueryFilter(u =>
            BypassCompanyFilter || u.CompanyId == CurrentCompanyId);

        // Lease — filtered unique index to prevent two Active leases on the
        // same Unit (LEASE_AUDIT Flaw #5). The application-level "Vacant"
        // check has a TOCTOU race; the DB constraint is the safety net.
        modelBuilder.Entity<Lease>(entity =>
        {
            entity.HasIndex(l => l.UnitId)
                .HasFilter("[Status] = 'Active'")
                .HasDatabaseName("UQ_Leases_UnitId_Active")
                .IsUnique();
        });

        // AuditLog — polymorphic record table. Indexed for the two main read
        // patterns: by entity (drill-down view) and by date (recent activity).
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.Property(a => a.EntityType).HasMaxLength(50).IsRequired();
            entity.Property(a => a.Action).HasMaxLength(50).IsRequired();
            entity.Property(a => a.ByUser).HasMaxLength(256).IsRequired();
            entity.HasIndex(a => new { a.EntityType, a.EntityId });
            entity.HasIndex(a => a.At);
        });

        // ClosedPeriod — one row per closed fiscal period. Period is the
        // natural key (yyyy-MM); unique index enforces that no period is
        // closed twice.
        modelBuilder.Entity<ClosedPeriod>(entity =>
        {
            entity.Property(p => p.Period).HasMaxLength(7).IsRequired();
            entity.Property(p => p.ClosedBy).HasMaxLength(256).IsRequired();
            entity.HasIndex(p => p.Period).IsUnique();
        });

        // DocumentLink — many-to-many bridge between Document and the four
        // owning entities. EntityType+EntityId is a polymorphic FK so we keep
        // it in the bridge instead of four nullable columns on Document.
        // Cascade delete from Document so removing a doc drops its links;
        // never the other way (deleting a tenant must not drop their docs —
        // operators keep them to fulfil GDPR export obligations).
        modelBuilder.Entity<DocumentLink>(entity =>
        {
            entity.HasOne(l => l.Document)
                .WithMany(d => d.Links)
                .HasForeignKey(l => l.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            // No duplicate links to the same (doc, entityType, entityId).
            entity.HasIndex(l => new { l.DocumentId, l.EntityType, l.EntityId })
                .IsUnique();

            // Most read queries filter by entity → speed up the lookup.
            entity.HasIndex(l => new { l.EntityType, l.EntityId });

            entity.Property(l => l.EntityType).HasMaxLength(50).IsRequired();
        });

        // Company configuration
        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasKey(e => e.CompanyId);
            entity.Property(e => e.CompanyName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Phone).HasMaxLength(50);
            entity.HasIndex(e => e.Email);
        });

        // ApplicationUser configuration
        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.LastName).IsRequired().HasMaxLength(100);

            entity.HasOne(e => e.Company)
                .WithMany(c => c.Users)
                .HasForeignKey(e => e.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Property configuration
        modelBuilder.Entity<Property>(entity =>
        {
            entity.HasKey(e => e.PropertyId);
            entity.Property(e => e.PropertyName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Address).IsRequired().HasMaxLength(300);
            entity.Property(e => e.PropertyValue).HasColumnType("decimal(18,2)");

            entity.HasOne(e => e.Company)
                .WithMany(c => c.Properties)
                .HasForeignKey(e => e.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Manager)
                .WithMany(u => u.ManagedProperties)
                .HasForeignKey(e => e.ManagerId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.CompanyId);
        });

        // Unit configuration
        modelBuilder.Entity<Unit>(entity =>
        {
            entity.HasKey(e => e.UnitId);
            entity.Property(e => e.UnitNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.MonthlyRent).HasColumnType("decimal(18,2)");

            entity.HasOne(e => e.Property)
                .WithMany(p => p.Units)
                .HasForeignKey(e => e.PropertyId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.PropertyId, e.UnitNumber }).IsUnique();
        });

        // Tenant configuration
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(e => e.TenantId);
            entity.Property(e => e.FirstName).HasMaxLength(100);
            entity.Property(e => e.LastName).HasMaxLength(100);
            entity.Property(e => e.Email).HasMaxLength(200);
            entity.Property(e => e.Phone).HasMaxLength(50);

            entity.HasOne(e => e.Company)
                .WithMany(c => c.Tenants)
                .HasForeignKey(e => e.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.Email);
            entity.HasIndex(e => e.CompanyId);
        });

        // Lease configuration
        modelBuilder.Entity<Lease>(entity =>
        {
            entity.HasKey(e => e.LeaseId);
            entity.Property(e => e.MonthlyRent).HasColumnType("decimal(18,2)");
            entity.Property(e => e.SecurityDeposit).HasColumnType("decimal(18,2)");
            entity.Property(e => e.LateFeeAmount).HasColumnType("decimal(18,2)");

            entity.HasOne(e => e.Unit)
                .WithMany(u => u.Leases)
                .HasForeignKey(e => e.UnitId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.Leases)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.LeaseEndDate);
        });

        // Payment configuration
        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(e => e.PaymentId);
            entity.Property(e => e.AmountPaid).HasColumnType("decimal(18,3)");   // Red-team #10: KWD uses 3-decimal fils
            entity.Property(e => e.LateFeeAmount).HasColumnType("decimal(18,3)");

            entity.HasOne(e => e.Invoice)
                .WithMany()
                .HasForeignKey(e => e.InvoiceId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Lease)
                .WithMany(l => l.Payments)
                .HasForeignKey(e => e.LeaseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.Payments)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Property)
                .WithMany()
                .HasForeignKey(e => e.PropertyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.PaymentDate);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.PaymentType);
        });

        // CashPayment configuration
        modelBuilder.Entity<CashPayment>(entity =>
        {
            entity.HasKey(e => e.CashPaymentId);
            entity.Property(e => e.Amount).HasColumnType("decimal(18,2)");

            entity.HasOne(e => e.Payment)
                .WithOne(p => p.CashPayment)
                .HasForeignKey<CashPayment>(e => e.PaymentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Tenant)
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Property)
                .WithMany()
                .HasForeignKey(e => e.PropertyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Invoice)
                .WithMany()
                .HasForeignKey(e => e.InvoiceId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.PaymentDate);
            entity.HasIndex(e => e.ReceiptNumber);
        });

        // BankTransfer configuration
        modelBuilder.Entity<BankTransfer>(entity =>
        {
            entity.HasKey(e => e.BankTransferId);
            entity.Property(e => e.Amount).HasColumnType("decimal(18,2)");
            entity.Property(e => e.BankName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.ProofFilePath).IsRequired().HasMaxLength(500);

            entity.HasOne(e => e.Payment)
                .WithOne(p => p.BankTransfer)
                .HasForeignKey<BankTransfer>(e => e.PaymentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Tenant)
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Property)
                .WithMany()
                .HasForeignKey(e => e.PropertyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Invoice)
                .WithMany()
                .HasForeignKey(e => e.InvoiceId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.SubmittedDate);
            entity.HasIndex(e => e.ReferenceNumber);
        });

        // MaintenanceRequest configuration
        modelBuilder.Entity<MaintenanceRequest>(entity =>
        {
            entity.HasKey(e => e.RequestId);
            entity.Property(e => e.EstimatedCost).HasColumnType("decimal(18,2)");
            entity.Property(e => e.ActualCost).HasColumnType("decimal(18,2)");

            entity.HasOne(e => e.Property)
                .WithMany(p => p.MaintenanceRequests)
                .HasForeignKey(e => e.PropertyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Unit)
                .WithMany()
                .HasForeignKey(e => e.UnitId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.MaintenanceRequests)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.AssignedToUser)
                .WithMany()
                .HasForeignKey(e => e.AssignedToUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.Priority);
        });

        // Invoice configuration
        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasKey(e => e.InvoiceId);
            entity.Property(e => e.TotalAmount).HasColumnType("decimal(18,3)");   // KWD 3-decimal fils
            entity.Property(e => e.PaidAmount).HasColumnType("decimal(18,3)");

            entity.HasOne(e => e.Lease)
                .WithMany(l => l.Invoices)
                .HasForeignKey(e => e.LeaseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Tenant)
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.DueDate);
            // Red-team #6: one invoice per (lease, type, period). InvoiceDate is
            // normalized to the 1st of the month by the generation job, so this
            // hard-stops the duplicate-monthly-invoice race.
            entity.HasIndex(e => new { e.LeaseId, e.InvoiceType, e.InvoiceDate })
                .IsUnique()
                .HasDatabaseName("UQ_Invoices_Lease_Type_Period");
        });

        // Document configuration
        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(e => e.DocumentId);
            entity.Property(e => e.DocumentName).IsRequired().HasMaxLength(300);
            entity.Property(e => e.DocumentType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.FilePath).IsRequired().HasMaxLength(500);

            entity.HasOne(e => e.UploadedByUser)
                .WithMany()
                .HasForeignKey(e => e.UploadedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.DocumentType);
            entity.HasIndex(e => e.UploadedDate);
        });

        // User configuration (custom user table for login)
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(200);
            entity.Property(e => e.PasswordHash).IsRequired().HasMaxLength(500);
            entity.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.LastName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.PhoneNumber).HasMaxLength(50);
            entity.Property(e => e.Role).IsRequired().HasMaxLength(50);

            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
        });

        // PaymentAudit configuration
        modelBuilder.Entity<PaymentAudit>(entity =>
        {
            entity.HasKey(e => e.PaymentAuditId);
            entity.Property(e => e.AuditType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.MismatchType).HasMaxLength(100);
            entity.Property(e => e.Amount).HasColumnType("decimal(18,2)");

            entity.HasOne(e => e.Payment)
                .WithMany()
                .HasForeignKey(e => e.PaymentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.AuditTimestamp);
            entity.HasIndex(e => e.IsMismatch);
            entity.HasIndex(e => e.IsResolved);
        });

        // PaymentIdempotency configuration
        modelBuilder.Entity<PaymentIdempotency>(entity =>
        {
            entity.HasKey(e => e.PaymentIdempotencyId);
            entity.Property(e => e.IdempotencyKey).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Amount).HasColumnType("decimal(18,2)");
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.CivilID).HasMaxLength(50);

            entity.HasOne(e => e.Payment)
                .WithMany()
                .HasForeignKey(e => e.PaymentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.IdempotencyKey).IsUnique();
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.ExpiresAt);
        });

        // KNETReconciliation configuration
        modelBuilder.Entity<KNETReconciliation>(entity =>
        {
            entity.HasKey(e => e.KNETReconciliationId);
            entity.Property(e => e.Amount).HasColumnType("decimal(18,2)");
            entity.Property(e => e.SettledAmount).HasColumnType("decimal(18,2)");
            entity.Property(e => e.DiscrepancyAmount).HasColumnType("decimal(18,2)");
            entity.Property(e => e.Currency).IsRequired().HasMaxLength(10);
            entity.Property(e => e.ReconciliationStatus).IsRequired().HasMaxLength(50);
            entity.Property(e => e.TransactionId).HasMaxLength(100);
            entity.Property(e => e.CheckoutPaymentId).HasMaxLength(100);
            entity.Property(e => e.KNETAccountID).HasMaxLength(100);
            entity.Property(e => e.KNETMID).HasMaxLength(100);
            entity.Property(e => e.BankName).HasMaxLength(200);
            entity.Property(e => e.BankAccountNumber).HasMaxLength(100);

            entity.HasOne(e => e.Payment)
                .WithMany()
                .HasForeignKey(e => e.PaymentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Property)
                .WithMany()
                .HasForeignKey(e => e.PropertyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.TransactionId);
            entity.HasIndex(e => e.CheckoutPaymentId);
            entity.HasIndex(e => e.ReconciliationStatus);
            entity.HasIndex(e => e.TransactionDate);
        });

        // Add new indexes for Payment entity new fields
        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasIndex(e => e.CivilID);
            entity.HasIndex(e => e.IdempotencyKey);
            entity.HasIndex(e => e.HasMismatch);
            entity.HasIndex(e => e.CheckoutPaymentId);
        });

        // Add index for Tenant GovernmentId (Civil ID)
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasIndex(e => e.GovernmentId);
        });
    }
}
