using FluentAssertions;
using FluentValidation.TestHelper;
using RentCollection.Application.DTOs;
using RentCollection.Application.Validators;

namespace RentCollection.Tests;

public class KnetPaymentValidatorTests
{
    private readonly KnetPaymentRequestValidator _validator = new();

    private static KnetPaymentRequestDto Valid() => new()
    {
        InvoiceId = 1,
        TenantId = 1,
        PropertyId = 1,
        LeaseId = 1,
        Amount = 100m,
        Language = "en"
    };

    [Fact]
    public void Valid_payload_passes()
    {
        _validator.TestValidate(Valid()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Zero_or_negative_amount_fails(decimal amount)
    {
        var req = Valid();
        req.Amount = amount;
        _validator.TestValidate(req).ShouldHaveValidationErrorFor(r => r.Amount);
    }

    [Fact]
    public void Amount_above_cap_fails()
    {
        var req = Valid();
        req.Amount = 1_000_000m;
        _validator.TestValidate(req).ShouldHaveValidationErrorFor(r => r.Amount);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("123")]
    public void Bad_civil_id_fails(string civilId)
    {
        var req = Valid();
        req.CivilID = civilId;
        _validator.TestValidate(req).ShouldHaveValidationErrorFor(r => r.CivilID);
    }

    [Fact]
    public void Missing_ids_fail()
    {
        var req = Valid();
        req.InvoiceId = 0;
        req.TenantId = 0;
        req.PropertyId = 0;
        req.LeaseId = 0;
        var result = _validator.TestValidate(req);
        result.ShouldHaveValidationErrorFor(r => r.InvoiceId);
        result.ShouldHaveValidationErrorFor(r => r.TenantId);
        result.ShouldHaveValidationErrorFor(r => r.PropertyId);
        result.ShouldHaveValidationErrorFor(r => r.LeaseId);
    }
}
