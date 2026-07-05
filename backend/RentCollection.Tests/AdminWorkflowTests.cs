using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using RentCollection.Application.DTOs;
using RentCollection.Application.Interfaces;
using RentCollection.Infrastructure.Services;
using RentCollection.Tests.Helpers;

namespace RentCollection.Tests;

/// <summary>
/// Tests for Admin Workflow:
/// 1. View all payments with mismatch flags
/// 2. Filter payments by various criteria
/// 3. Detect and flag mismatches (Civil ID, Building, KNETMID)
/// 4. Resolve mismatches
/// 5. View reconciliation summary
/// 6. View payment audit history
/// </summary>
public class AdminWorkflowTests : IDisposable
{
    private readonly Infrastructure.Data.ApplicationDbContext _context;
    private readonly Mock<ILogger<PaymentVerificationService>> _mockLogger;
    private readonly PaymentVerificationService _paymentVerificationService;

    public AdminWorkflowTests()
    {
        _context = TestDatabaseHelper.CreateInMemoryContext(Guid.NewGuid().ToString());
        _mockLogger = new Mock<ILogger<PaymentVerificationService>>();

        _paymentVerificationService = new PaymentVerificationService(
            _context,
            _mockLogger.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    #region Payment Verification Tests

    [Fact]
    public async Task GetPaymentsForVerification_WithNoFilter_ReturnsAllPayments()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);

        // Act
        var payments = await _paymentVerificationService.GetPaymentsForVerificationAsync();

        // Assert
        payments.Should().NotBeNull();
        payments.Should().HaveCount(3); // 3 payments in seed data
    }

    [Fact]
    public async Task GetPaymentsForVerification_ReturnsCorrectPaymentDetails()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);

        // Act
        var payments = await _paymentVerificationService.GetPaymentsForVerificationAsync();

        // Assert
        var firstPayment = payments.FirstOrDefault(p => p.PaymentId == 1);
        firstPayment.Should().NotBeNull();
        firstPayment!.TenantName.Should().Be("Ahmed Al-Rashid");
        firstPayment.TenantCivilID.Should().Be("284010100001");
        firstPayment.BuildingName.Should().Be("Al Salmiya Tower");
        firstPayment.Amount.Should().Be(450);
        firstPayment.BuildingKNETMID.Should().Be("MID_SALMIYA_001");
        firstPayment.PaymentKNETMID.Should().Be("MID_SALMIYA_001");
    }

    [Fact]
    public async Task GetPaymentsForVerification_SortedByDateDescending()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);

        // Act
        var payments = await _paymentVerificationService.GetPaymentsForVerificationAsync();

        // Assert
        payments.Should().BeInDescendingOrder(p => p.PaymentDate);
    }

    #endregion

    #region Mismatch Detection Tests

    [Fact]
    public async Task GetPaymentsForVerification_DetectsCivilIDMismatch()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);

        // Act
        var payments = await _paymentVerificationService.GetPaymentsForVerificationAsync();

        // Assert
        var mismatchPayment = payments.FirstOrDefault(p => p.PaymentId == 2);
        mismatchPayment.Should().NotBeNull();
        mismatchPayment!.HasMismatch.Should().BeTrue();
        mismatchPayment.CivilIDMismatch.Should().BeTrue();
        mismatchPayment.PaymentCivilID.Should().Be("999999999999");
        mismatchPayment.TenantCivilID.Should().Be("284010100001");
    }

    [Fact]
    public async Task GetPaymentsForVerification_DetectsKNETMIDMismatch()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);

        // Act
        var payments = await _paymentVerificationService.GetPaymentsForVerificationAsync();

        // Assert
        var mismatchPayment = payments.FirstOrDefault(p => p.PaymentId == 3);
        mismatchPayment.Should().NotBeNull();
        mismatchPayment!.HasMismatch.Should().BeTrue();
        mismatchPayment.KNETMIDMismatch.Should().BeTrue();
        mismatchPayment.BuildingKNETMID.Should().Be("MID_HAWALLY_002");
        mismatchPayment.PaymentKNETMID.Should().Be("MID_SALMIYA_001"); // Wrong!
    }

    [Fact]
    public async Task GetPaymentsForVerification_NoMismatchWhenAllMatch()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);

        // Act
        var payments = await _paymentVerificationService.GetPaymentsForVerificationAsync();

        // Assert
        var normalPayment = payments.FirstOrDefault(p => p.PaymentId == 1);
        normalPayment.Should().NotBeNull();
        normalPayment!.HasMismatch.Should().BeFalse();
        normalPayment.CivilIDMismatch.Should().BeFalse();
        normalPayment.KNETMIDMismatch.Should().BeFalse();
        normalPayment.BuildingMismatch.Should().BeFalse();
    }

    [Fact]
    public async Task GetMismatchedPayments_ReturnsOnlyMismatchedPayments()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);

        // Act
        var mismatchedPayments = await _paymentVerificationService.GetMismatchedPaymentsAsync();

        // Assert
        mismatchedPayments.Should().NotBeNull();
        mismatchedPayments.Should().HaveCount(2); // 2 payments with mismatches
        mismatchedPayments.All(p => p.HasMismatch).Should().BeTrue();
    }

    #endregion

    #region Filter Tests

    [Fact]
    public async Task GetPaymentsForVerification_FilterByCivilID()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var filter = new PaymentVerificationFilterDto
        {
            CivilID = "284010100001"
        };

        // Act
        var payments = await _paymentVerificationService.GetPaymentsForVerificationAsync(filter);

        // Assert
        payments.Should().NotBeNull();
        payments.Should().HaveCountGreaterThan(0);
        // All payments should be for Ahmed (either as tenant or payment Civil ID)
    }

    [Fact]
    public async Task GetPaymentsForVerification_FilterByLeaseId()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var filter = new PaymentVerificationFilterDto
        {
            LeaseId = 1
        };

        // Act
        var payments = await _paymentVerificationService.GetPaymentsForVerificationAsync(filter);

        // Assert
        payments.Should().NotBeNull();
        payments.All(p => p.LeaseId == 1).Should().BeTrue();
    }

    [Fact]
    public async Task GetPaymentsForVerification_FilterByPropertyId()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var filter = new PaymentVerificationFilterDto
        {
            PropertyId = 1 // Salmiya
        };

        // Act
        var payments = await _paymentVerificationService.GetPaymentsForVerificationAsync(filter);

        // Assert
        payments.Should().NotBeNull();
        payments.All(p => p.PropertyId == 1).Should().BeTrue();
    }

    [Fact]
    public async Task GetPaymentsForVerification_FilterByKNETMID()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var filter = new PaymentVerificationFilterDto
        {
            KNETMID = "MID_SALMIYA_001"
        };

        // Act
        var payments = await _paymentVerificationService.GetPaymentsForVerificationAsync(filter);

        // Assert
        payments.Should().NotBeNull();
        payments.All(p => p.PaymentKNETMID == "MID_SALMIYA_001").Should().BeTrue();
    }

    [Fact]
    public async Task GetPaymentsForVerification_FilterByHasMismatch()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var filter = new PaymentVerificationFilterDto
        {
            HasMismatch = true
        };

        // Act
        var payments = await _paymentVerificationService.GetPaymentsForVerificationAsync(filter);

        // Assert
        payments.Should().NotBeNull();
        payments.Should().HaveCount(2);
        payments.All(p => p.HasMismatch).Should().BeTrue();
    }

    [Fact]
    public async Task GetPaymentsForVerification_FilterByDateRange()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var filter = new PaymentVerificationFilterDto
        {
            StartDate = DateTime.UtcNow.AddDays(-10),
            EndDate = DateTime.UtcNow
        };

        // Act
        var payments = await _paymentVerificationService.GetPaymentsForVerificationAsync(filter);

        // Assert
        payments.Should().NotBeNull();
        payments.All(p => p.PaymentDate >= filter.StartDate && p.PaymentDate <= filter.EndDate).Should().BeTrue();
    }

    [Fact]
    public async Task GetPaymentsForVerification_FilterByStatus()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var filter = new PaymentVerificationFilterDto
        {
            Status = "Completed"
        };

        // Act
        var payments = await _paymentVerificationService.GetPaymentsForVerificationAsync(filter);

        // Assert
        payments.Should().NotBeNull();
        payments.All(p => p.Status == "Completed").Should().BeTrue();
    }

    [Fact]
    public async Task GetPaymentsForVerification_CombinedFilters()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var filter = new PaymentVerificationFilterDto
        {
            PropertyId = 1,
            HasMismatch = true
        };

        // Act
        var payments = await _paymentVerificationService.GetPaymentsForVerificationAsync(filter);

        // Assert
        payments.Should().NotBeNull();
        payments.All(p => p.PropertyId == 1 && p.HasMismatch).Should().BeTrue();
    }

    #endregion

    #region Mismatch Resolution Tests

    [Fact]
    public async Task ResolveMismatch_WithValidPayment_ResolvesSuccessfully()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var paymentId = 2; // Payment with Civil ID mismatch
        var resolvedBy = "admin@test.com";
        var resolutionNotes = "Verified with tenant - authorized family member made payment";

        // Act
        var result = await _paymentVerificationService.ResolveMismatchAsync(paymentId, resolvedBy, resolutionNotes);

        // Assert
        result.Should().BeTrue();

        // Verify payment is updated
        var payment = await _context.Payments.FindAsync(paymentId);
        payment.Should().NotBeNull();
        payment!.HasMismatch.Should().BeFalse();
        payment.MismatchDetails.Should().Contain("RESOLVED");
        payment.MismatchDetails.Should().Contain(resolvedBy);
    }

    [Fact]
    public async Task ResolveMismatch_UpdatesAuditRecords()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var paymentId = 2;
        var resolvedBy = "admin@test.com";
        var resolutionNotes = "Verified with tenant";

        // Act
        await _paymentVerificationService.ResolveMismatchAsync(paymentId, resolvedBy, resolutionNotes);

        // Assert
        var auditRecords = _context.PaymentAudits.Where(a => a.PaymentId == paymentId).ToList();
        auditRecords.Should().NotBeEmpty();

        var originalAudit = auditRecords.FirstOrDefault(a => a.AuditType == "CivilIDMismatch");
        originalAudit.Should().NotBeNull();
        originalAudit!.IsResolved.Should().BeTrue();
        originalAudit.ResolvedBy.Should().Be(resolvedBy);
        originalAudit.ResolvedDate.Should().NotBeNull();

        // Resolution audit should be created
        var resolutionAudit = auditRecords.FirstOrDefault(a => a.AuditType == "MismatchResolved");
        resolutionAudit.Should().NotBeNull();
    }

    [Fact]
    public async Task ResolveMismatch_WithNonExistentPayment_ReturnsFalse()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var paymentId = 999;
        var resolvedBy = "admin@test.com";

        // Act
        var result = await _paymentVerificationService.ResolveMismatchAsync(paymentId, resolvedBy, null);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Payment Audit History Tests

    [Fact]
    public async Task GetPaymentAuditHistory_ReturnsAuditRecords()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var paymentId = 2; // Has audit record

        // Act
        var auditHistory = await _paymentVerificationService.GetPaymentAuditHistoryAsync(paymentId);

        // Assert
        auditHistory.Should().NotBeNull();
        auditHistory.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public async Task GetPaymentAuditHistory_SortedByTimestampDescending()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        // First resolve the mismatch to create more audit records
        await _paymentVerificationService.ResolveMismatchAsync(2, "admin@test.com", "Resolved");
        var paymentId = 2;

        // Act
        var auditHistory = await _paymentVerificationService.GetPaymentAuditHistoryAsync(paymentId);

        // Assert
        auditHistory.Should().BeInDescendingOrder(a => a.AuditTimestamp);
    }

    [Fact]
    public async Task GetPaymentAuditHistory_ContainsMismatchDetails()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var paymentId = 2;

        // Act
        var auditHistory = await _paymentVerificationService.GetPaymentAuditHistoryAsync(paymentId);

        // Assert
        var mismatchAudit = auditHistory.FirstOrDefault(a => a.IsMismatch);
        mismatchAudit.Should().NotBeNull();
        mismatchAudit!.MismatchType.Should().Be("CivilID");
        mismatchAudit.ExpectedValue.Should().Contain("284010100001");
        mismatchAudit.ActualValue.Should().Contain("999999999999");
    }

    #endregion

    #region Reconciliation Summary Tests

    [Fact]
    public async Task GetReconciliationSummary_ReturnsSummaryForDateRange()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var startDate = DateTime.UtcNow.AddMonths(-3);
        var endDate = DateTime.UtcNow;

        // Act
        var summary = await _paymentVerificationService.GetReconciliationSummaryAsync(startDate, endDate);

        // Assert
        summary.Should().NotBeNull();
        summary.StartDate.Should().Be(startDate);
        summary.EndDate.Should().Be(endDate);
        summary.TotalPayments.Should().BeGreaterThan(0);
        summary.TotalAmount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetReconciliationSummary_CalculatesCompletedPayments()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var startDate = DateTime.UtcNow.AddMonths(-3);
        var endDate = DateTime.UtcNow;

        // Act
        var summary = await _paymentVerificationService.GetReconciliationSummaryAsync(startDate, endDate);

        // Assert
        summary.CompletedPayments.Should().BeGreaterThan(0);
        summary.CompletedAmount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetReconciliationSummary_CalculatesMismatchedPayments()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var startDate = DateTime.UtcNow.AddMonths(-3);
        var endDate = DateTime.UtcNow;

        // Act
        var summary = await _paymentVerificationService.GetReconciliationSummaryAsync(startDate, endDate);

        // Assert
        summary.MismatchedPayments.Should().Be(2); // 2 payments with mismatches
        summary.MismatchedAmount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetReconciliationSummary_GroupsByBuilding()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var startDate = DateTime.UtcNow.AddMonths(-3);
        var endDate = DateTime.UtcNow;

        // Act
        var summary = await _paymentVerificationService.GetReconciliationSummaryAsync(startDate, endDate);

        // Assert
        summary.ByBuilding.Should().NotBeNull();
        summary.ByBuilding.Should().HaveCountGreaterThan(0);

        var salmiyaBuilding = summary.ByBuilding.FirstOrDefault(b => b.PropertyName == "Al Salmiya Tower");
        salmiyaBuilding.Should().NotBeNull();
        salmiyaBuilding!.KNETAccountID.Should().Be("KNET_ACC_001");
        salmiyaBuilding.KNETMID.Should().Be("MID_SALMIYA_001");
    }

    [Fact]
    public async Task GetReconciliationSummary_GroupsByKNETAccount()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var startDate = DateTime.UtcNow.AddMonths(-3);
        var endDate = DateTime.UtcNow;

        // Act
        var summary = await _paymentVerificationService.GetReconciliationSummaryAsync(startDate, endDate);

        // Assert
        summary.ByKNETAccount.Should().NotBeNull();
        summary.ByKNETAccount.Should().HaveCountGreaterThan(0);

        foreach (var account in summary.ByKNETAccount)
        {
            account.KNETAccountID.Should().NotBeNullOrEmpty();
            account.PaymentCount.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public async Task GetReconciliationSummary_FilterByPropertyId()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var startDate = DateTime.UtcNow.AddMonths(-3);
        var endDate = DateTime.UtcNow;
        var propertyId = 1; // Salmiya

        // Act
        var summary = await _paymentVerificationService.GetReconciliationSummaryAsync(startDate, endDate, propertyId);

        // Assert
        summary.Should().NotBeNull();
        summary.PropertyId.Should().Be(propertyId);
        summary.PropertyName.Should().Be("Al Salmiya Tower");

        // All payments should be from this property
        summary.ByBuilding.Should().HaveCount(1);
        summary.ByBuilding.First().PropertyId.Should().Be(propertyId);
    }

    [Fact]
    public async Task GetReconciliationSummary_CalculatesUnresolvedMismatches()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var startDate = DateTime.UtcNow.AddMonths(-3);
        var endDate = DateTime.UtcNow;

        // Act
        var summary = await _paymentVerificationService.GetReconciliationSummaryAsync(startDate, endDate);

        // Assert
        summary.UnresolvedMismatches.Should().BeGreaterThan(0);
        summary.ResolvedMismatches.Should().BeGreaterThanOrEqualTo(0);
    }

    #endregion

    #region KNET Reconciliation Data Tests

    [Fact]
    public async Task GetKNETReconciliationData_WithNoData_ReturnsEmptyList()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var startDate = DateTime.UtcNow.AddDays(-30);
        var endDate = DateTime.UtcNow;

        // Act
        var data = await _paymentVerificationService.GetKNETReconciliationDataAsync(startDate, endDate);

        // Assert
        data.Should().NotBeNull();
        // Empty because KNETReconciliation table is not seeded
    }

    #endregion
}
