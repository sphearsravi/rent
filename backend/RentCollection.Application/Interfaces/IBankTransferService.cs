using RentCollection.Application.DTOs;

namespace RentCollection.Application.Interfaces;

/// <summary>
/// Bank transfer service interface (SOLID: Single Responsibility Principle)
/// Handles only bank transfer operations including submission and approval workflow
/// </summary>
public interface IBankTransferService
{
    Task<BankTransferResponseDto> SubmitBankTransferAsync(BankTransferSubmitDto submitDto, string submittedBy);
    Task<BankTransferResponseDto?> GetBankTransferByIdAsync(int bankTransferId);
    Task<IEnumerable<BankTransferResponseDto>> GetPendingBankTransfersAsync();
    Task<IEnumerable<BankTransferResponseDto>> GetBankTransfersByPropertyAsync(int propertyId);
    Task<IEnumerable<BankTransferResponseDto>> GetBankTransfersByTenantAsync(int tenantId);
    Task<BankTransferResponseDto> ApproveBankTransferAsync(int bankTransferId, string approvedBy);
    Task<BankTransferResponseDto> RejectBankTransferAsync(int bankTransferId, string rejectedBy, string rejectionReason);
    Task<string> GetBankTransferProofFilePathAsync(int bankTransferId);
}
