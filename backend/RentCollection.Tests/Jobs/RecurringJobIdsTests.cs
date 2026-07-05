using System.Reflection;
using FluentAssertions;
using RentCollection.Application.Interfaces;
using RentCollection.Infrastructure.Services;

namespace RentCollection.Tests.Jobs;

/// <summary>
/// Guards against name drift between three places that all reference recurring
/// job IDs:
///   1. <see cref="RecurringJobIds"/> — the canonical constants
///   2. <see cref="RecurringJobs"/> — the method that does the work
///   3. <c>Program.cs</c> — the registration with Hangfire (verified indirectly
///      via the integration test that boots the app)
///
/// If somebody renames <c>FlipExpiredLeasesAsync</c> without updating the
/// constant — or vice versa — the deploy succeeds, the Hangfire dashboard
/// looks fine, and at the next 01:00 the wrong code runs (or no code runs).
/// These tests catch that at <c>dotnet test</c> time instead.
/// </summary>
public class RecurringJobIdsTests
{
    [Fact]
    public void All_ids_are_distinct_kebab_case_strings()
    {
        RecurringJobIds.All.Should().OnlyHaveUniqueItems();
        foreach (var id in RecurringJobIds.All)
        {
            id.Should().NotBeNullOrWhiteSpace();
            id.Should().MatchRegex("^[a-z]+(-[a-z]+)+$",
                "job IDs are kebab-case to match Hangfire dashboard conventions");
        }
    }

    [Fact]
    public void All_ids_map_to_a_real_method_on_RecurringJobs()
    {
        foreach (var id in RecurringJobIds.All)
        {
            var methodName = RecurringJobIds.MethodNameFor(id);
            var method = typeof(RecurringJobs)
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);

            method.Should().NotBeNull(
                $"job ID '{id}' must map to a public instance method on RecurringJobs " +
                $"(expected: {methodName}). Either rename the method, rename the ID, " +
                $"or update RecurringJobIds.MethodNameFor.");

            // Must be async-shaped so Hangfire can await the returned Task.
            method!.ReturnType.Should().Be(typeof(Task),
                $"job method '{methodName}' must return Task for Hangfire to await it");

            method.GetParameters().Should().BeEmpty(
                $"job method '{methodName}' must be parameterless — Hangfire " +
                "AddOrUpdate calls it with no arguments");
        }
    }

    [Fact]
    public void MethodNameFor_throws_for_unknown_ids()
    {
        var act = () => RecurringJobIds.MethodNameFor("nuke-the-database");
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Unknown recurring job ID*");
    }

    [Fact]
    public void All_constants_appear_in_the_All_array()
    {
        // Reflection sweep: every `public const string` on RecurringJobIds
        // must appear in `All`. Catches the "added a constant but forgot to
        // append to All" mistake.
        var constants = typeof(RecurringJobIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        constants.Should().NotBeEmpty();
        RecurringJobIds.All.Should().BeEquivalentTo(constants);
    }
}
