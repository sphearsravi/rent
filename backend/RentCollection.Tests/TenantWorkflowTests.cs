using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using RentCollection.Application.DTOs;
using RentCollection.Application.Interfaces;
using RentCollection.Infrastructure.Services;
using RentCollection.Tests.Helpers;

namespace RentCollection.Tests;

/// <summary>
/// Tests for Tenant Workflow:
/// 1. Login with Civil ID
/// 2. View multiple leases across different buildings
/// 3. Select lease and view payment information
/// 4. Validate Civil ID matches lease owner
/// </summary>
public class TenantWorkflowTests : IDisposable
{
    private readonly Infrastructure.Data.ApplicationDbContext _context;
    private readonly Mock<IJwtTokenService> _mockJwtTokenService;
    private readonly Mock<ILogger<TenantLeaseService>> _mockLogger;
    private readonly TenantLeaseService _tenantLeaseService;

    public TenantWorkflowTests()
    {
        _context = TestDatabaseHelper.CreateInMemoryContext(Guid.NewGuid().ToString());
        _mockJwtTokenService = new Mock<IJwtTokenService>();
        _mockLogger = new Mock<ILogger<TenantLeaseService>>();

        // Setup JWT token service mock
        _mockJwtTokenService
            .Setup(x => x.GenerateToken(It.IsAny<int>(), It.IsAny<string>()))
            .Returns("test_jwt_token_12345");

        _tenantLeaseService = new TenantLeaseService(
            _context,
            _mockJwtTokenService.Object,
            _mockLogger.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    #region Login Tests

    [Fact]
    public async Task LoginByCivilId_WithValidCredentials_ReturnsSuccessWithToken()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var request = new TenantLoginRequestDto
        {
            CivilID = "284010100001", // Ahmed's Civil ID
            Password = "Test@123"
        };

        // Act
        var result = await _tenantLeaseService.LoginByCivilIdAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Token.Should().NotBeNullOrEmpty();
        result.Token.Should().Be("test_jwt_token_12345");
        result.Tenant.Should().NotBeNull();
        result.Tenant!.CivilID.Should().Be("284010100001");
        result.Tenant.FullName.Should().Be("Ahmed Al-Rashid");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task LoginByCivilId_WithInvalidCivilId_ReturnsFailure()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var request = new TenantLoginRequestDto
        {
            CivilID = "999999999999", // Non-existent Civil ID
            Password = "Test@123"
        };

        // Act
        var result = await _tenantLeaseService.LoginByCivilIdAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Token.Should().BeNull();
        result.ErrorMessage.Should().Contain("Invalid Civil ID");
    }

    [Fact]
    public async Task LoginByCivilId_WithInvalidPassword_ReturnsFailure()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var request = new TenantLoginRequestDto
        {
            CivilID = "284010100001",
            Password = "WrongPassword"
        };

        // Act
        var result = await _tenantLeaseService.LoginByCivilIdAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Token.Should().BeNull();
        result.ErrorMessage.Should().Contain("Invalid");
    }

    [Fact]
    public async Task LoginByCivilId_WithInactiveTenant_ReturnsFailure()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var request = new TenantLoginRequestDto
        {
            CivilID = "284030300003", // Mohammed's Civil ID (Inactive)
            Password = "Test@789"
        };

        // Act
        var result = await _tenantLeaseService.LoginByCivilIdAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("inactive");
    }

    #endregion

    #region Multi-Lease Tests

    [Fact]
    public async Task LoginByCivilId_TenantWithMultipleLeases_ReturnsAllActiveLeases()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var request = new TenantLoginRequestDto
        {
            CivilID = "284010100001", // Ahmed has 3 active leases
            Password = "Test@123"
        };

        // Act
        var result = await _tenantLeaseService.LoginByCivilIdAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.ActiveLeases.Should().NotBeNull();
        result.ActiveLeases.Should().HaveCount(3); // Ahmed has 3 leases

        // Verify leases are in different buildings
        var buildingNames = result.ActiveLeases.Select(l => l.BuildingName).Distinct().ToList();
        buildingNames.Should().Contain("Al Salmiya Tower");
        buildingNames.Should().Contain("Hawally Plaza");
        buildingNames.Should().Contain("Farwaniya Center");
    }

    [Fact]
    public async Task GetActiveLeasesByCivilId_ReturnsLeasesWithBuildingKNETInfo()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var civilId = "284010100001"; // Ahmed

        // Act
        var leases = await _tenantLeaseService.GetActiveLeasesByCivilIdAsync(civilId);

        // Assert
        leases.Should().NotBeNull();
        leases.Should().HaveCount(3);

        // Check Salmiya lease has KNET info
        var salmiyaLease = leases.FirstOrDefault(l => l.BuildingName == "Al Salmiya Tower");
        salmiyaLease.Should().NotBeNull();
        salmiyaLease!.AcceptKNET.Should().BeTrue();
        salmiyaLease.KNETAccountID.Should().Be("KNET_ACC_001");
        salmiyaLease.KNETMID.Should().Be("MID_SALMIYA_001");

        // Check Hawally lease has different KNET info
        var hawallyLease = leases.FirstOrDefault(l => l.BuildingName == "Hawally Plaza");
        hawallyLease.Should().NotBeNull();
        hawallyLease!.AcceptKNET.Should().BeTrue();
        hawallyLease.KNETAccountID.Should().Be("KNET_ACC_002");
        hawallyLease.KNETMID.Should().Be("MID_HAWALLY_002");

        // Check Farwaniya lease has NO KNET
        var farwaniyaLease = leases.FirstOrDefault(l => l.BuildingName == "Farwaniya Center");
        farwaniyaLease.Should().NotBeNull();
        farwaniyaLease!.AcceptKNET.Should().BeFalse();
        farwaniyaLease.KNETAccountID.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveLeasesByCivilId_ReturnsOutstandingAmounts()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var civilId = "284010100001"; // Ahmed

        // Act
        var leases = await _tenantLeaseService.GetActiveLeasesByCivilIdAsync(civilId);

        // Assert
        var salmiyaLease = leases.FirstOrDefault(l => l.BuildingName == "Al Salmiya Tower");
        salmiyaLease.Should().NotBeNull();
        salmiyaLease!.TotalOutstanding.Should().Be(900); // 2 unpaid invoices of 450 each
        salmiyaLease.PendingInvoicesCount.Should().Be(2);
    }

    #endregion

    #region Lease Payment Info Tests

    [Fact]
    public async Task GetLeasePaymentInfo_WithValidLeaseAndCivilId_ReturnsPaymentInfo()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var leaseId = 1;
        var civilId = "284010100001"; // Ahmed

        // Act
        var result = await _tenantLeaseService.GetLeasePaymentInfoAsync(leaseId, civilId);

        // Assert
        result.Should().NotBeNull();
        result!.LeaseId.Should().Be(1);
        result.CivilID.Should().Be(civilId);
        result.BuildingName.Should().Be("Al Salmiya Tower");
        result.MonthlyRent.Should().Be(450);
        result.KNETAccountID.Should().Be("KNET_ACC_001");
        result.KNETMID.Should().Be("MID_SALMIYA_001");
        result.KnetTranportalId.Should().NotBeNullOrEmpty();
        result.PendingInvoices.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetLeasePaymentInfo_WithDifferentBuilding_ReturnsDifferentKNETCredentials()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var leaseId = 2; // Ahmed's Hawally lease
        var civilId = "284010100001";

        // Act
        var result = await _tenantLeaseService.GetLeasePaymentInfoAsync(leaseId, civilId);

        // Assert
        result.Should().NotBeNull();
        result!.BuildingName.Should().Be("Hawally Plaza");
        result.KNETAccountID.Should().Be("KNET_ACC_002");
        result.KNETMID.Should().Be("MID_HAWALLY_002");
        // Different credentials than Building 1
        result.KNETMID.Should().NotBe("MID_SALMIYA_001");
    }

    [Fact]
    public async Task GetLeasePaymentInfo_WithWrongCivilId_ReturnsNull()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var leaseId = 1; // Ahmed's lease
        var civilId = "284020200002"; // Fatima's Civil ID

        // Act
        var result = await _tenantLeaseService.GetLeasePaymentInfoAsync(leaseId, civilId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLeasePaymentInfo_WithNonExistentLease_ReturnsNull()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var leaseId = 999;
        var civilId = "284010100001";

        // Act
        var result = await _tenantLeaseService.GetLeasePaymentInfoAsync(leaseId, civilId);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Civil ID Validation Tests

    [Fact]
    public async Task ValidateCivilIdForLease_WithMatchingCivilId_ReturnsValid()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var civilId = "284010100001"; // Ahmed
        var leaseId = 1; // Ahmed's lease

        // Act
        var (isValid, errorMessage) = await _tenantLeaseService.ValidateCivilIdForLeaseAsync(civilId, leaseId);

        // Assert
        isValid.Should().BeTrue();
        errorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ValidateCivilIdForLease_WithMismatchedCivilId_ReturnsInvalid()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var civilId = "284020200002"; // Fatima's Civil ID
        var leaseId = 1; // Ahmed's lease

        // Act
        var (isValid, errorMessage) = await _tenantLeaseService.ValidateCivilIdForLeaseAsync(civilId, leaseId);

        // Assert
        isValid.Should().BeFalse();
        errorMessage.Should().Contain("does not match");
        errorMessage.Should().Contain("284010100001"); // Expected Civil ID
    }

    [Fact]
    public async Task ValidateCivilIdForLease_WithNonExistentLease_ReturnsInvalid()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var civilId = "284010100001";
        var leaseId = 999;

        // Act
        var (isValid, errorMessage) = await _tenantLeaseService.ValidateCivilIdForLeaseAsync(civilId, leaseId);

        // Assert
        isValid.Should().BeFalse();
        errorMessage.Should().Contain("not found");
    }

    #endregion

    #region Pending Invoices Tests

    [Fact]
    public async Task GetPendingInvoicesForLease_ReturnsUnpaidInvoices()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var leaseId = 1;

        // Act
        var invoices = await _tenantLeaseService.GetPendingInvoicesForLeaseAsync(leaseId);

        // Assert
        invoices.Should().NotBeNull();
        invoices.Should().HaveCount(2);
        invoices.All(i => i.Status != "Paid").Should().BeTrue();
        invoices.Should().BeInAscendingOrder(i => i.DueDate);
    }

    [Fact]
    public async Task GetPendingInvoicesForLease_IdentifiesOverdueInvoices()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var leaseId = 1;

        // Act
        var invoices = await _tenantLeaseService.GetPendingInvoicesForLeaseAsync(leaseId);

        // Assert
        var overdueInvoice = invoices.FirstOrDefault(i => i.Status == "Overdue");
        overdueInvoice.Should().NotBeNull();
        overdueInvoice!.IsOverdue.Should().BeTrue();
        overdueInvoice.OutstandingAmount.Should().Be(450);
    }

    #endregion

    #region Payment Receipt Tests

    [Fact]
    public async Task GetPaymentReceipt_WithValidPaymentId_ReturnsReceipt()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var paymentId = 1;

        // Act
        var receipt = await _tenantLeaseService.GetPaymentReceiptAsync(paymentId);

        // Assert
        receipt.Should().NotBeNull();
        receipt!.PaymentId.Should().Be(1);
        receipt.ReceiptNumber.Should().Contain("RCPT");
        receipt.TenantName.Should().Be("Ahmed Al-Rashid");
        receipt.CivilID.Should().Be("284010100001");
        receipt.BuildingName.Should().Be("Al Salmiya Tower");
        receipt.TotalAmount.Should().Be(450);
        receipt.KNETAccountID.Should().Be("KNET_ACC_001");
        receipt.KNETMID.Should().Be("MID_SALMIYA_001");
    }

    [Fact]
    public async Task GetPaymentReceipt_WithNonExistentPayment_ReturnsNull()
    {
        // Arrange
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var paymentId = 999;

        // Act
        var receipt = await _tenantLeaseService.GetPaymentReceiptAsync(paymentId);

        // Assert
        receipt.Should().BeNull();
    }

    #endregion
}
