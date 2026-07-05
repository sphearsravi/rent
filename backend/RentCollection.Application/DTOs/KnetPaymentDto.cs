namespace RentCollection.Application.DTOs;

public class KnetPaymentRequestDto
{
    public int InvoiceId { get; set; }
    public int TenantId { get; set; }
    public int PropertyId { get; set; }  // BuildingID
    public int LeaseId { get; set; }
    public decimal Amount { get; set; }
    public string? Language { get; set; } = "en";
    public string? SuccessUrl { get; set; }
    public string? FailureUrl { get; set; }

    // Civil ID & Building KNET routing
    public string? CivilID { get; set; }
    public string? IdempotencyKey { get; set; }  // Client-generated unique key to prevent duplicates
}

public class KnetPaymentResponseDto
{
    public bool Success { get; set; }
    public string? PaymentId { get; set; }
    public string? KnetPaymentId { get; set; }
    public string? RedirectUrl { get; set; }
    public string? Status { get; set; }
    public string? ErrorMessage { get; set; }
    public string? TransactionReference { get; set; }

    // Building & KNET routing info
    public int? PropertyId { get; set; }
    public string? BuildingName { get; set; }
    public string? KNETAccountID { get; set; }
    public string? KNETMID { get; set; }

    // Mismatch detection
    public bool HasMismatch { get; set; }
    public string? MismatchDetails { get; set; }
}

/// <summary>
/// Parsed KNET gateway callback response parameters
/// </summary>
public class KnetGatewayResponseDto
{
    public string? Result { get; set; }       // "CAPTURED", "NOT CAPTURED", etc.
    public string? PaymentId { get; set; }    // KNET payment ID
    public string? TransactionId { get; set; } // KNET transaction ID
    public string? TrackId { get; set; }       // Merchant track ID (our reference)
    public string? AuthCode { get; set; }      // Authorization code
    public string? ReferenceId { get; set; }   // KNET reference ID
    public string? PostDate { get; set; }      // Transaction post date
    public decimal? Amount { get; set; }       // Transaction amount
    public string? Udf1 { get; set; }
    public string? Udf2 { get; set; }
    public string? Udf3 { get; set; }
    public string? Udf4 { get; set; }
    public string? Udf5 { get; set; }
    public string? Error { get; set; }
    public string? ErrorText { get; set; }
}
