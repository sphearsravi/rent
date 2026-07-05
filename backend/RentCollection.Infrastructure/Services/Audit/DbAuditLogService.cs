using System.Text.Json;
using Microsoft.Extensions.Logging;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Infrastructure.Services.Audit;

/// <summary>
/// EF-backed audit-log writer. Adds an <see cref="AuditLog"/> row to the
/// current DbContext but does NOT call SaveChanges — the caller already owns
/// a transaction (e.g. <c>LeasesController.Renew</c>) and we want the log
/// to commit or roll back with the action it describes.
/// </summary>
public class DbAuditLogService : IAuditLogService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<DbAuditLogService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public DbAuditLogService(ApplicationDbContext db, ILogger<DbAuditLogService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task WriteAsync(
        string entityType,
        int entityId,
        string action,
        string byUser,
        object? payload = null,
        CancellationToken ct = default)
    {
        string? payloadJson = null;
        if (payload != null)
        {
            try { payloadJson = JsonSerializer.Serialize(payload, JsonOpts); }
            catch (Exception ex)
            {
                // Audit must never break the calling transaction. Log the
                // serialisation failure and store an empty object so the
                // row still records who/when/what.
                _logger.LogWarning(ex,
                    "AuditLog payload serialisation failed for {EntityType}#{EntityId} {Action}",
                    entityType, entityId, action);
                payloadJson = "{}";
            }
        }

        _db.AuditLogs.Add(new AuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            ByUser = string.IsNullOrWhiteSpace(byUser) ? "system" : byUser,
            At = DateTime.UtcNow,
            PayloadJson = payloadJson,
        });

        // Synchronous-shaped return — there's no async work because we don't
        // touch the DB until the caller's SaveChangesAsync runs.
        return Task.CompletedTask;
    }
}
