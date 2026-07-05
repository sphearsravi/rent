using RentCollection.Application.DTOs;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Core.Enums;

namespace RentCollection.Application.Services;

/// <summary>
/// Bank transfer service implementation (SOLID: Single Responsibility + Dependency Inversion)
/// </summary>
public class BankTransferService : IBankTransferService
{
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<BankTransfer> _bankTransferRepository;
    private readonly IRepository<Invoice> _invoiceRepository;
    private readonly IRepository<Tenant> _tenantRepository;
    private readonly IRepository<Property> _propertyRepository;
    private readonly IFileStorageService _fileStorageService;

    public BankTransferService(
        IRepository<Payment> paymentRepository,
        IRepository<BankTransfer> bankTransferRepository,
        IRepository<Invoice> invoiceRepository,
        IRepository<Tenant> tenantRepository,
        IRepository<Property> propertyRepository,
        IFileStorageService fileStorageService)
    {
        _paymentRepository = paymentRepository;
        _bankTransferRepository = bankTransferRepository;
        _invoiceRepository = invoiceRepository;
        _tenantRepository = tenantRepository;
        _propertyRepository = propertyRepository;
        _fileStorageService = fileStorageService;
    }

    public async Task<BankTransferResponseDto> SubmitBankTransferAsync(BankTransferSubmitDto submitDto, string submittedBy)
    {
        // Validate file
        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".pdf" };
        if (!_fileStorageService.IsValidFileType(submitDto.ProofFile.FileName, allowedExtensions))
            throw new ArgumentException("Invalid file type. Only JPG, PNG, and PDF files are allowed.");

        if (!_fileStorageService.IsValidFileSize(submitDto.ProofFile.Length, 5 * 1024 * 1024)) // 5MB
            throw new ArgumentException("File size exceeds maximum limit of 5MB.");

        // Validate invoice if provided and resolve the lease from it so the
        // bank-transfer Payment row participates in lease-level reconciliation
        // (parity with the cash + KNET paths).
        int leaseId = 0;
        if (submitDto.InvoiceId.HasValue)
        {
            var invoice = await _invoiceRepository.GetByIdAsync(submitDto.InvoiceId.Value);
            if (invoice == null)
                throw new ArgumentException($"Invoice with ID {submitDto.InvoiceId} not found.");

            if (invoice.Status == "Paid")
                throw new InvalidOperationException($"Invoice {submitDto.InvoiceId} is already paid.");

            leaseId = invoice.LeaseId;
        }

        // Save uploaded file
        var folderPath = $"BankTransfers/{submitDto.TenantId}";
        var filePath = await _fileStorageService.SaveFileAsync(submitDto.ProofFile, folderPath);

        // Create payment record
        var payment = new Payment
        {
            InvoiceId = submitDto.InvoiceId,
            TenantId = submitDto.TenantId,
            PropertyId = submitDto.PropertyId,
            LeaseId = leaseId,
            PaymentDate = submitDto.TransferDate,
            AmountPaid = submitDto.Amount,
            PaymentType = PaymentType.BankTransfer,
            Status = PaymentStatus.PendingVerification,
            TransactionReference = submitDto.ReferenceNumber,
            Notes = submitDto.Notes,
            CreatedDate = DateTime.UtcNow
        };

        var createdPayment = await _paymentRepository.AddAsync(payment);

        // Create bank transfer record
        var bankTransfer = new BankTransfer
        {
            PaymentId = createdPayment.PaymentId,
            TenantId = submitDto.TenantId,
            PropertyId = submitDto.PropertyId,
            InvoiceId = submitDto.InvoiceId,
            Amount = submitDto.Amount,
            BankName = submitDto.BankName,
            TransferDate = submitDto.TransferDate,
            ReferenceNumber = submitDto.ReferenceNumber,
            ProofFilePath = filePath,
            ProofFileName = submitDto.ProofFile.FileName,
            ProofFileType = _fileStorageService.GetFileExtension(submitDto.ProofFile.FileName),
            ProofFileSize = submitDto.ProofFile.Length,
            Status = BankTransferStatus.PendingVerification,
            Notes = submitDto.Notes,
            SubmittedBy = submittedBy,
            SubmittedDate = DateTime.UtcNow
        };

        var createdBankTransfer = await _bankTransferRepository.AddAsync(bankTransfer);

        // Update invoice status to pending verification if applicable
        if (submitDto.InvoiceId.HasValue)
        {
            var invoice = await _invoiceRepository.GetByIdAsync(submitDto.InvoiceId.Value);
            if (invoice != null)
            {
                invoice.Status = "Pending Verification";
                await _invoiceRepository.UpdateAsync(invoice);
            }
        }

        // Get tenant and property names
        var tenant = await _tenantRepository.GetByIdAsync(submitDto.TenantId);
        var property = await _propertyRepository.GetByIdAsync(submitDto.PropertyId);

        return new BankTransferResponseDto
        {
            BankTransferId = createdBankTransfer.BankTransferId,
            PaymentId = createdPayment.PaymentId,
            TenantId = submitDto.TenantId,
            TenantName = tenant != null ? $"{tenant.FirstName} {tenant.LastName}".Trim() : "Unknown",
            PropertyId = submitDto.PropertyId,
            PropertyName = property?.PropertyName ?? "Unknown",
            Amount = submitDto.Amount,
            BankName = submitDto.BankName,
            TransferDate = submitDto.TransferDate,
            ReferenceNumber = submitDto.ReferenceNumber,
            ProofFilePath = filePath,
            Status = BankTransferStatus.PendingVerification.ToString(),
            SubmittedDate = bankTransfer.SubmittedDate
        };
    }

    public async Task<BankTransferResponseDto?> GetBankTransferByIdAsync(int bankTransferId)
    {
        var bankTransfer = await _bankTransferRepository.GetByIdAsync(bankTransferId);
        if (bankTransfer == null) return null;

        var tenant = await _tenantRepository.GetByIdAsync(bankTransfer.TenantId);
        var property = await _propertyRepository.GetByIdAsync(bankTransfer.PropertyId);

        return new BankTransferResponseDto
        {
            BankTransferId = bankTransfer.BankTransferId,
            PaymentId = bankTransfer.PaymentId,
            TenantId = bankTransfer.TenantId,
            TenantName = tenant != null ? $"{tenant.FirstName} {tenant.LastName}".Trim() : "Unknown",
            PropertyId = bankTransfer.PropertyId,
            PropertyName = property?.PropertyName ?? "Unknown",
            Amount = bankTransfer.Amount,
            BankName = bankTransfer.BankName,
            TransferDate = bankTransfer.TransferDate,
            ReferenceNumber = bankTransfer.ReferenceNumber,
            ProofFilePath = bankTransfer.ProofFilePath,
            Status = bankTransfer.Status.ToString(),
            SubmittedDate = bankTransfer.SubmittedDate,
            ApprovedBy = bankTransfer.ApprovedBy,
            ApprovedDate = bankTransfer.ApprovedDate,
            RejectionReason = bankTransfer.RejectionReason
        };
    }

    public async Task<IEnumerable<BankTransferResponseDto>> GetPendingBankTransfersAsync()
    {
        var bankTransfers = await _bankTransferRepository.GetAllAsync(bt =>
            bt.Status == BankTransferStatus.PendingVerification);

        var result = new List<BankTransferResponseDto>();
        foreach (var bt in bankTransfers)
        {
            var tenant = await _tenantRepository.GetByIdAsync(bt.TenantId);
            var property = await _propertyRepository.GetByIdAsync(bt.PropertyId);

            result.Add(new BankTransferResponseDto
            {
                BankTransferId = bt.BankTransferId,
                PaymentId = bt.PaymentId,
                TenantId = bt.TenantId,
                TenantName = tenant != null ? $"{tenant.FirstName} {tenant.LastName}".Trim() : "Unknown",
                PropertyId = bt.PropertyId,
                PropertyName = property?.PropertyName ?? "Unknown",
                Amount = bt.Amount,
                BankName = bt.BankName,
                TransferDate = bt.TransferDate,
                ReferenceNumber = bt.ReferenceNumber,
                ProofFilePath = bt.ProofFilePath,
                Status = bt.Status.ToString(),
                SubmittedDate = bt.SubmittedDate
            });
        }

        return result;
    }

    public async Task<IEnumerable<BankTransferResponseDto>> GetBankTransfersByPropertyAsync(int propertyId)
    {
        var bankTransfers = await _bankTransferRepository.GetAllAsync(bt => bt.PropertyId == propertyId);

        var result = new List<BankTransferResponseDto>();
        foreach (var bt in bankTransfers)
        {
            var tenant = await _tenantRepository.GetByIdAsync(bt.TenantId);
            var property = await _propertyRepository.GetByIdAsync(bt.PropertyId);

            result.Add(new BankTransferResponseDto
            {
                BankTransferId = bt.BankTransferId,
                PaymentId = bt.PaymentId,
                TenantId = bt.TenantId,
                TenantName = tenant != null ? $"{tenant.FirstName} {tenant.LastName}".Trim() : "Unknown",
                PropertyId = bt.PropertyId,
                PropertyName = property?.PropertyName ?? "Unknown",
                Amount = bt.Amount,
                BankName = bt.BankName,
                TransferDate = bt.TransferDate,
                ReferenceNumber = bt.ReferenceNumber,
                ProofFilePath = bt.ProofFilePath,
                Status = bt.Status.ToString(),
                SubmittedDate = bt.SubmittedDate,
                ApprovedBy = bt.ApprovedBy,
                ApprovedDate = bt.ApprovedDate,
                RejectionReason = bt.RejectionReason
            });
        }

        return result;
    }

    public async Task<IEnumerable<BankTransferResponseDto>> GetBankTransfersByTenantAsync(int tenantId)
    {
        var bankTransfers = await _bankTransferRepository.GetAllAsync(bt => bt.TenantId == tenantId);

        var result = new List<BankTransferResponseDto>();
        foreach (var bt in bankTransfers)
        {
            var tenant = await _tenantRepository.GetByIdAsync(bt.TenantId);
            var property = await _propertyRepository.GetByIdAsync(bt.PropertyId);

            result.Add(new BankTransferResponseDto
            {
                BankTransferId = bt.BankTransferId,
                PaymentId = bt.PaymentId,
                TenantId = bt.TenantId,
                TenantName = tenant != null ? $"{tenant.FirstName} {tenant.LastName}".Trim() : "Unknown",
                PropertyId = bt.PropertyId,
                PropertyName = property?.PropertyName ?? "Unknown",
                Amount = bt.Amount,
                BankName = bt.BankName,
                TransferDate = bt.TransferDate,
                ReferenceNumber = bt.ReferenceNumber,
                ProofFilePath = bt.ProofFilePath,
                Status = bt.Status.ToString(),
                SubmittedDate = bt.SubmittedDate,
                ApprovedBy = bt.ApprovedBy,
                ApprovedDate = bt.ApprovedDate,
                RejectionReason = bt.RejectionReason
            });
        }

        return result;
    }

    public async Task<BankTransferResponseDto> ApproveBankTransferAsync(int bankTransferId, string approvedBy)
    {
        var bankTransfer = await _bankTransferRepository.GetByIdAsync(bankTransferId);
        if (bankTransfer == null)
            throw new ArgumentException($"Bank transfer with ID {bankTransferId} not found.");

        if (bankTransfer.Status != BankTransferStatus.PendingVerification)
            throw new InvalidOperationException($"Bank transfer is not in pending status.");

        // Update bank transfer status
        bankTransfer.Status = BankTransferStatus.Approved;
        bankTransfer.ApprovedBy = approvedBy;
        bankTransfer.ApprovedDate = DateTime.UtcNow;
        await _bankTransferRepository.UpdateAsync(bankTransfer);

        // Update payment status
        var payment = await _paymentRepository.GetByIdAsync(bankTransfer.PaymentId);
        if (payment != null)
        {
            payment.Status = PaymentStatus.Completed;
            payment.ApprovedBy = approvedBy;
            payment.ApprovedDate = DateTime.UtcNow;
            payment.ReceiptNumber = await GenerateReceiptNumberAsync();
            await _paymentRepository.UpdateAsync(payment);
        }

        // Update invoice status if applicable
        if (bankTransfer.InvoiceId.HasValue)
        {
            var invoice = await _invoiceRepository.GetByIdAsync(bankTransfer.InvoiceId.Value);
            if (invoice != null)
            {
                invoice.PaidAmount += bankTransfer.Amount;
                if (invoice.PaidAmount >= invoice.TotalAmount)
                {
                    invoice.Status = "Paid";
                }
                else
                {
                    invoice.Status = "Partially Paid";
                }
                await _invoiceRepository.UpdateAsync(invoice);
            }
        }

        return (await GetBankTransferByIdAsync(bankTransferId))!;
    }

    public async Task<BankTransferResponseDto> RejectBankTransferAsync(int bankTransferId, string rejectedBy, string rejectionReason)
    {
        var bankTransfer = await _bankTransferRepository.GetByIdAsync(bankTransferId);
        if (bankTransfer == null)
            throw new ArgumentException($"Bank transfer with ID {bankTransferId} not found.");

        if (bankTransfer.Status != BankTransferStatus.PendingVerification)
            throw new InvalidOperationException($"Bank transfer is not in pending status.");

        // Update bank transfer status
        bankTransfer.Status = BankTransferStatus.Rejected;
        bankTransfer.ApprovedBy = rejectedBy;
        bankTransfer.ApprovedDate = DateTime.UtcNow;
        bankTransfer.RejectionReason = rejectionReason;
        await _bankTransferRepository.UpdateAsync(bankTransfer);

        // Update payment status
        var payment = await _paymentRepository.GetByIdAsync(bankTransfer.PaymentId);
        if (payment != null)
        {
            payment.Status = PaymentStatus.Rejected;
            await _paymentRepository.UpdateAsync(payment);
        }

        // Update invoice status if applicable
        if (bankTransfer.InvoiceId.HasValue)
        {
            var invoice = await _invoiceRepository.GetByIdAsync(bankTransfer.InvoiceId.Value);
            if (invoice != null)
            {
                invoice.Status = "Pending";
                await _invoiceRepository.UpdateAsync(invoice);
            }
        }

        return (await GetBankTransferByIdAsync(bankTransferId))!;
    }

    public async Task<string> GetBankTransferProofFilePathAsync(int bankTransferId)
    {
        var bankTransfer = await _bankTransferRepository.GetByIdAsync(bankTransferId);
        if (bankTransfer == null)
            throw new ArgumentException($"Bank transfer with ID {bankTransferId} not found.");

        return bankTransfer.ProofFilePath;
    }

    private async Task<string> GenerateReceiptNumberAsync()
    {
        var count = await _bankTransferRepository.CountAsync();
        return $"BANK-{DateTime.Now:yyyyMMdd}-{(count + 1):D5}";
    }
}
