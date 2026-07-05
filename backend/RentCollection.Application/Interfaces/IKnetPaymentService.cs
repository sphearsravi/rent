using RentCollection.Application.DTOs;

namespace RentCollection.Application.Interfaces;

public interface IKnetPaymentService
{
    /// <summary>
    /// Initiates a KNET payment and returns a redirect URL for the customer
    /// </summary>
    Task<KnetPaymentResponseDto> InitiatePaymentAsync(KnetPaymentRequestDto request);

    /// <summary>
    /// Handles the response POST from KNET gateway after payment completion
    /// </summary>
    Task<KnetPaymentResponseDto> HandleKnetResponseAsync(
        string? trandata,
        string? paymentId,
        string? trackId,
        string? tranId,
        string? result,
        string? error,
        string? errorText = null);

    /// <summary>
    /// TEST MODE: Completes a test payment (simulates successful/failed payment)
    /// </summary>
    Task<KnetPaymentResponseDto> CompleteTestPaymentAsync(int paymentId, bool success = true);
}
