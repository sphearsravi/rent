using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace RentCollection.IntegrationTests;

/// <summary>
/// Sprint 12 — pin the MVC option that fixes the "silent form submit" bug
/// the user reported (created lease not appearing).
///
/// Background: EF navigation properties on Lease / Unit / Payment etc. are
/// declared <c>public X X { get; set; } = null!;</c> for compiler null-safety.
/// ASP.NET Core's metadata system treats non-nullable reference types as
/// implicit <c>[Required]</c>, so when a form POSTs only the FK (e.g.
/// <c>UnitId</c>), the navigation property is null at validation time and
/// ModelState fails with "The Unit field is required." — but the message
/// renders in the page chrome quietly and the user thinks the submit
/// succeeded.
///
/// The fix is one line in <c>Program.cs</c>: set
/// <c>SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true</c>.
/// This test enforces that setting at boot so a refactor can't silently
/// re-enable the bug.
/// </summary>
public sealed class MvcOptionsTests : IClassFixture<InMemoryAppFactory>
{
    private readonly InMemoryAppFactory _factory;
    public MvcOptionsTests(InMemoryAppFactory factory) => _factory = factory;

    [Fact]
    public void SuppressImplicitRequiredAttributeForNonNullableReferenceTypes_must_be_true()
    {
        // Resolve MvcOptions from the test host's DI exactly as MVC reads
        // them at request time. If the setting flips back to false, the
        // assertion fires loud and points at the cause.
        var mvcOptions = _factory.Services.GetRequiredService<IOptions<MvcOptions>>().Value;

        mvcOptions.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes.Should().BeTrue(
            "EF navigation properties like Lease.Unit/Tenant or Unit.Property are " +
            "declared `null!` for compiler null-safety, NOT as a server-side validation " +
            "requirement. Without this setting, every entity-bound form POST silently " +
            "fails ModelState validation because the navigations aren't populated " +
            "from the form. Reported by user 2026-06-02: created lease not appearing.");
    }
}
