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
        // Validate file — extension AND real content (audit H11). The extension
        // check is a fast pre-filter; the magic-byte check is the real gate, since
        // the client controls the filename/content-type.
        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".pdf" };
        if (!_fileStorageService.IsValidFileType(submitDto.ProofFile.FileName, allowedExtensions))
            throw new ArgumentException("Invalid file type. Only JPG, PNG, and PDF files are allowed.");

        if (!FileSignatureValidator.IsAllowed(submitDto.ProofFile))
            throw new ArgumentException("File content does not match an allowed image or PDF.");

        if (!_fileStorageService.IsValidFileSize(submitDto.ProofFile.Length, 5 * 1024 * 1024)) // 5MB
            throw new ArgumentException("File size exceeds maximum limit of 5MB.");

        // ── Money + date validation ──
        // KWD is 3-decimal (fils); round once here and use `amount` everywhere
        // below so Payment, BankTransfer and the response all agree to the fil.
        var amount = decimal.Round(submitDto.Amount, 3);
        if (amount <= 0)
            throw new ArgumentException("Amount must be greater than zero.");

        if (submitDto.TransferDate == default)
            throw new ArgumentException("Transfer date is required.");
        // +1 day of slack for timezone skew between the tenant's device and UTC.
        if (submitDto.TransferDate.Date > DateTime.UtcNow.Date.AddDays(1))
            throw new ArgumentException("Transfer date cannot be in the future.");

        // Validate invoice if provided and resolve the lease from it so the
        // bank-transfer Payment row participates in lease-level reconciliation
        // (parity with the cash + KNET paths).
        int leaseId = 0;
        Invoice? invoice = null;
        if (submitDto.InvoiceId.HasValue)
        {
            invoice = await _invoiceRepository.GetByIdAsync(submitDto.InvoiceId.Value);
            if (invoice == null)
                throw new ArgumentException($"Invoice with ID {submitDto.InvoiceId} not found.");

            if (invoice.Status == InvoiceStatuses.Paid)
                throw new InvalidOperationException($"Invoice {submitDto.InvoiceId} is already paid.");

            if (invoice.Status == InvoiceStatuses.Cancelled)
                throw new InvalidOperationException($"Invoice {submitDto.InvoiceId} is cancelled and cannot be paid.");

            // Never accept more than is owed — an over-submission would push
            // PaidAmount past TotalAmount on approval.
            var balance = decimal.Round(invoice.TotalAmount - invoice.PaidAmount, 3);
            if (amount > balance)
                throw new ArgumentException(
                    $"Amount {amount:0.000} KWD exceeds the outstanding balance of {balance:0.000} KWD on invoice {submitDto.InvoiceId}.");

            leaseId = invoice.LeaseId;
        }

        // Duplicate bank reference for the same tenant → almost always the same
        // slip uploaded twice. A previously REJECTED submission may be re-sent
        // with the same reference (e.g. the proof image was unreadable).
        var reference = string.IsNullOrWhiteSpace(submitDto.ReferenceNumber) ? null : submitDto.ReferenceNumber.Trim();
        if (reference != null)
        {
            var duplicate = await _bankTransferRepository.ExistsAsync(bt =>
                bt.TenantId == submitDto.TenantId
                && bt.ReferenceNumber == reference
                && bt.Status != BankTransferStatus.Rejected);
            if (duplicate)
                throw new ArgumentException(
                    $"A bank transfer with reference number '{reference}' has already been submitted for this tenant.");
        }

        // ── Persist ──
        // The repository abstraction gives no DbContext/transaction handle, so
        // the three writes can't share one transaction from this layer. Order
        // them so the proof file exists BEFORE any DB row references it, and
        // compensate (delete file, delete orphan payment) if a later step fails.
        var folderPath = $"BankTransfers/{submitDto.TenantId}";
        var filePath = await _fileStorageService.SaveFileAsync(submitDto.ProofFile, folderPath);

        Payment? createdPayment = null;
        BankTransfer createdBankTransfer;
        try
        {
            // Create payment record
            var payment = new Payment
            {
                InvoiceId = submitDto.InvoiceId,
                TenantId = submitDto.TenantId,
                PropertyId = submitDto.PropertyId,
                LeaseId = leaseId,
                PaymentDate = submitDto.TransferDate,
                AmountPaid = amount,
                PaymentType = PaymentType.BankTransfer,
                Status = PaymentStatus.PendingVerification,
                TransactionReference = reference,
                Notes = submitDto.Notes,
                CreatedDate = DateTime.UtcNow
            };

            createdPayment = await _paymentRepository.AddAsync(payment);

            // Create bank transfer record
            var bankTransfer = new BankTransfer
            {
                PaymentId = createdPayment.PaymentId,
                TenantId = submitDto.TenantId,
                PropertyId = submitDto.PropertyId,
                InvoiceId = submitDto.InvoiceId,
                Amount = amount,
                BankName = submitDto.BankName,
                TransferDate = submitDto.TransferDate,
                ReferenceNumber = reference,
                ProofFilePath = filePath,
                ProofFileName = submitDto.ProofFile.FileName,
                ProofFileType = _fileStorageService.GetFileExtension(submitDto.ProofFile.FileName),
                ProofFileSize = submitDto.ProofFile.Length,
                Status = BankTransferStatus.PendingVerification,
                Notes = submitDto.Notes,
                SubmittedBy = submittedBy,
                SubmittedDate = DateTime.UtcNow
            };

            createdBankTransfer = await _bankTransferRepository.AddAsync(bankTransfer);

            // Update invoice status to pending verification if applicable.
            // (Controllers depend on this transition; Cancelled was rejected above.)
            if (invoice != null)
            {
                invoice.Status = InvoiceStatuses.PendingVerification;
                await _invoiceRepository.UpdateAsync(invoice);
            }
        }
        catch
        {
            // Best-effort compensation; the original exception is what the
            // caller needs to see, so cleanup failures are swallowed.
            try { await _fileStorageService.DeleteFileAsync(filePath); } catch { /* ignore */ }
            if (createdPayment != null)
            {
                try { await _paymentRepository.DeleteAsync(createdPayment); } catch { /* ignore */ }
            }
            throw;
        }

        // Get tenant and property names
        var tenant = await _tenantRepository.GetByIdAsync(submitDto.TenantId);
        var property = await _propertyRepository.GetByIdAsync(submitDto.PropertyId);

        return new BankTransferResponseDto
        {
            BankTransferId = createdBankTransfer.BankTransferId,
            PaymentId = createdPayment!.PaymentId,   // non-null: the try block either assigned it or re-threw
            TenantId = submitDto.TenantId,
            TenantName = tenant != null ? $"{tenant.FirstName} {tenant.LastName}".Trim() : "Unknown",
            PropertyId = submitDto.PropertyId,
            PropertyName = property?.PropertyName ?? "Unknown",
            Amount = amount,
            BankName = submitDto.BankName,
            TransferDate = submitDto.TransferDate,
            ReferenceNumber = reference,
            ProofFilePath = filePath,
            Status = BankTransferStatus.PendingVerification.ToString(),
            SubmittedDate = createdBankTransfer.SubmittedDate
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
                    invoice.Status = InvoiceStatuses.Paid;
                }
                else
                {
                    invoice.Status = InvoiceStatuses.PartiallyPaid;
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
                invoice.Status = InvoiceStatuses.Pending;
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
