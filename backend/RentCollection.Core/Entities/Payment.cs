using RentCollection.Core.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RentCollection.Core.Entities;

public class Payment
{
    public int PaymentId { get; set; }
    public int? InvoiceId { get; set; }
    public int LeaseId { get; set; }
    public int TenantId { get; set; }
    public int PropertyId { get; set; }  // BuildingID - links to Property table
    public DateTime PaymentDate { get; set; }
    public decimal AmountPaid { get; set; }
    public PaymentType PaymentType { get; set; }
    public PaymentStatus Status { get; set; } = Enums.PaymentStatus.Pending;
    public string? TransactionReference { get; set; }
    public decimal? LateFeeAmount { get; set; }
    public string? Notes { get; set; }
    public string? CollectedBy { get; set; } // For cash payments - admin user who collected
    public string? ApprovedBy { get; set; } // For bank transfers - admin who approved
    public DateTime? ApprovedDate { get; set; }
    public string? ReceiptNumber { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedDate { get; set; }

    // Civil ID & KNET Tracking (New fields for multi-building support)
    public string? CivilID { get; set; }              // Tenant's Civil ID used for payment
    public string? KNETAccountIDUsed { get; set; }    // KNET Account ID used for this payment
    public string? KNETMIDUsed { get; set; }          // KNET Merchant ID used for this payment
    public string? CheckoutPaymentId { get; set; }    // Checkout.com payment ID for reconciliation

    // Idempotency & Audit
    public string? IdempotencyKey { get; set; }       // Unique key to prevent duplicate payments
    public bool HasMismatch { get; set; } = false;    // Flag if any validation mismatch detected
    public string? MismatchDetails { get; set; }      // Details of any mismatches found

    // Optimistic concurrency token — prevents two concurrent KNET callbacks
    // (e.g. /response + browser retry) from both flipping Status/Notes.
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    // Backward compatibility properties (not mapped to database)
    [NotMapped]
    public string PaymentMethod
    {
        get => PaymentType.ToString();
        set
        {
            if (Enum.TryParse<PaymentType>(value, true, out var paymentType))
                PaymentType = paymentType;
        }
    }

    [NotMapped]
    public string PaymentStatus
    {
        get => Status.ToString();
        set
        {
            if (Enum.TryParse<PaymentStatus>(value, true, out var status))
                Status = status;
        }
    }

    // Navigation properties
    public Invoice? Invoice { get; set; }
    public Lease Lease { get; set; } = null!;
    public Tenant Tenant { get; set; } = null!;
    public Property Property { get; set; } = null!;
    public CashPayment? CashPayment { get; set; }
    public BankTransfer? BankTransfer { get; set; }
}
