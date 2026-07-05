using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;
using RentCollection.Infrastructure.Services;
using RentCollection.Tests.Helpers;

namespace RentCollection.Tests.Jobs;

/// <summary>
/// Failure-injection tests for the notification outbox. The whole point of
/// the outbox pattern is that a transport failure (SMTP down, Mailgun
/// degraded) must NOT lose the notification — it must stay durable in the
/// table for the next worker pass to retry.
///
/// These tests use <see cref="NotificationService"/> directly (not via
/// <c>RecurringJobs.ProcessNotificationOutboxAsync</c>) so the assertion
/// boundary stays tight: we want to prove the dispatch loop's failure
/// handling, not Hangfire orchestration.
/// </summary>
public class NotificationOutboxFailureTests : IDisposable
{
    private readonly ApplicationDbContext _ctx;
    private readonly Mock<IEmailService> _email = new();
    private readonly Mock<IFcmService> _fcm = new();
    private readonly NotificationService _sut;

    public NotificationOutboxFailureTests()
    {
        _ctx = TestDatabaseHelper.CreateInMemoryContext($"Outbox_{Guid.NewGuid()}");
        _sut = new NotificationService(
            _ctx, _email.Object, _fcm.Object, NullLogger<NotificationService>.Instance);
    }

    public void Dispose() => _ctx.Dispose();

    private async Task<Notification> SeedPendingNotificationAsync(string subject = "Test")
    {
        _ctx.Companies.Add(new Company
        {
            CompanyId = 1, CompanyName = "T", Email = "t@t.com",
            Phone = "1", Address = "x", IsActive = true,
        });
        _ctx.Tenants.Add(new Tenant
        {
            TenantId = 1, CompanyId = 1,
            FirstName = "T", LastName = "X",
            Email = "tenant@example.com", Phone = "1",
            GovernmentId = "GID1", Status = "Active",
        });
        await _ctx.SaveChangesAsync();

        await _sut.EnqueueTenantAsync(
            tenantId: 1, subject: subject, body: "hello",
            relatedEntityType: "Test", relatedEntityId: 1);
        return _ctx.Set<Notification>().Single();
    }

    [Fact]
    public async Task Email_transport_failure_keeps_notification_in_outbox_with_error_recorded()
    {
        _email.Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailSendResult(false, "SMTP timeout"));

        await SeedPendingNotificationAsync();

        var sent = await _sut.ProcessPendingAsync();

        sent.Should().Be(0, "transport failure must not count as sent");
        var row = _ctx.Set<Notification>().AsNoTracking().Single();
        row.Status.Should().Be(NotificationStatus.Pending,
            "Pending stays Pending so the next worker pass retries");
        row.Attempts.Should().Be(1);
        row.LastError.Should().Be("SMTP timeout");
        row.SentAt.Should().BeNull();
        row.NextAttemptAt.Should().NotBeNull(
            "exponential-backoff timer must be set so the next pass doesn't retry instantly");
    }

    [Fact]
    public async Task Email_transport_exception_keeps_notification_in_outbox()
    {
        // A thrown exception is the SMTP equivalent of "the connection died
        // mid-send". The dispatcher must not let it bubble — that would crash
        // the Hangfire worker.
        _email.Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("connection reset"));

        await SeedPendingNotificationAsync();

        var sent = await _sut.ProcessPendingAsync();

        sent.Should().Be(0);
        var row = _ctx.Set<Notification>().AsNoTracking().Single();
        row.Status.Should().Be(NotificationStatus.Pending);
        row.Attempts.Should().Be(1);
        row.LastError.Should().Contain("connection reset");
    }

    [Fact]
    public async Task Five_consecutive_failures_mark_notification_as_Failed()
    {
        _email.Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailSendResult(false, "perma-down"));

        var row = await SeedPendingNotificationAsync();

        // Drain → fail → reset backoff timer → drain again, five times.
        for (int i = 0; i < 5; i++)
        {
            // Reset NextAttemptAt so the next ProcessPending picks the row up
            // even though the exponential backoff hasn't elapsed in real time.
            row = _ctx.Set<Notification>().Single();
            row.NextAttemptAt = DateTime.UtcNow.AddSeconds(-1);
            await _ctx.SaveChangesAsync();

            await _sut.ProcessPendingAsync();
        }

        var final = _ctx.Set<Notification>().AsNoTracking().Single();
        final.Status.Should().Be(NotificationStatus.Failed,
            "after Attempts ≥ 5 the dispatcher gives up and marks Failed");
        final.Attempts.Should().Be(5);
    }

    [Fact]
    public async Task Recovery_after_transient_failure_sends_on_next_pass()
    {
        // First call fails, second call succeeds — simulates SMTP coming back.
        var callCount = 0;
        _email.Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? new EmailSendResult(false, "transient")
                    : new EmailSendResult(true);
            });

        var row = await SeedPendingNotificationAsync();

        // First pass — fails.
        await _sut.ProcessPendingAsync();
        var afterFail = _ctx.Set<Notification>().AsNoTracking().Single();
        afterFail.Status.Should().Be(NotificationStatus.Pending);

        // Reset NextAttemptAt to bypass backoff in the test, then retry.
        row = _ctx.Set<Notification>().Single();
        row.NextAttemptAt = DateTime.UtcNow.AddSeconds(-1);
        await _ctx.SaveChangesAsync();

        await _sut.ProcessPendingAsync();

        var afterRetry = _ctx.Set<Notification>().AsNoTracking().Single();
        afterRetry.Status.Should().Be(NotificationStatus.Sent,
            "transient failure followed by success must mark Sent");
        afterRetry.SentAt.Should().NotBeNull();
        afterRetry.Attempts.Should().Be(2);
        afterRetry.LastError.Should().BeNull("success clears the prior error");
    }

    [Fact]
    public async Task Backoff_timer_blocks_immediate_retry_within_the_same_pass()
    {
        // ProcessPending only picks up rows where NextAttemptAt <= now.
        // After a failure NextAttemptAt is set to UtcNow + several minutes,
        // so a second ProcessPending call immediately after must be a no-op.
        _email.Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailSendResult(false, "down"));

        await SeedPendingNotificationAsync();

        await _sut.ProcessPendingAsync(); // 1 → fails, NextAttemptAt = +4 min
        await _sut.ProcessPendingAsync(); // 2 → no-op, NextAttemptAt not elapsed
        await _sut.ProcessPendingAsync(); // 3 → no-op

        // Email transport called once, not three times.
        _email.Verify(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        var row = _ctx.Set<Notification>().AsNoTracking().Single();
        row.Attempts.Should().Be(1, "backoff prevented retries 2 and 3 from running");
    }
}
