using FluentValidation;
using RentCollection.Application.DTOs;

namespace RentCollection.Application.Validators;

/// <summary>
/// FluentValidation rules for KnetPaymentRequestDto.
/// The service still re-validates amount/ownership against the database — these
/// rules just reject malformed payloads early.
/// </summary>
public class KnetPaymentRequestValidator : AbstractValidator<KnetPaymentRequestDto>
{
    public KnetPaymentRequestValidator()
    {
        RuleFor(r => r.InvoiceId).GreaterThan(0);
        RuleFor(r => r.TenantId).GreaterThan(0);
        RuleFor(r => r.PropertyId).GreaterThan(0);
        RuleFor(r => r.LeaseId).GreaterThan(0);
        // KWD has 3 decimal places — reject zero/negative; the service further
        // checks the amount against the invoice's remaining balance.
        RuleFor(r => r.Amount)
            .GreaterThan(0m)
            .LessThanOrEqualTo(100_000m).WithMessage("Amount exceeds the per-transaction cap.");
        RuleFor(r => r.Language)
            .Must(l => string.IsNullOrEmpty(l) || l == "en" || l == "ar")
            .WithMessage("Language must be 'en' or 'ar'.");
        RuleFor(r => r.CivilID)
            .Matches("^[0-9]{8,12}$").When(r => !string.IsNullOrEmpty(r.CivilID))
            .WithMessage("CivilID must be 8-12 digits.");
        RuleFor(r => r.IdempotencyKey)
            .MaximumLength(128).When(r => !string.IsNullOrEmpty(r.IdempotencyKey));
    }
}
