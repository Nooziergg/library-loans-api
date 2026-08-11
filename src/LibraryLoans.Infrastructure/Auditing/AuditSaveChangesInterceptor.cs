using System.Text.Json;
using LibraryLoans.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LibraryLoans.Infrastructure.Auditing;

/// <summary>
/// Writes the audit trail, once, for every change the application saves.
///
/// <para><b>Why an interceptor and not a line in each handler.</b> An audit trail is only worth
/// having if it is complete, and "every handler remembers to call the audit service" is a rule that
/// holds until the fifteenth handler is written under deadline pressure. The gap it leaves is
/// invisible — nothing fails, a row simply has no history — and it is discovered during the incident
/// that needed it. Hanging the trail off <c>SaveChanges</c> inverts that: a change cannot reach the
/// database without passing through here, so a new aggregate is audited on the day it is added and
/// nobody has to remember anything.</para>
///
/// <para><b>Why it writes into the same unit of work.</b> The audit rows are added to the same change
/// tracker, so they are inserted by the same <c>SaveChanges</c>, inside the same transaction, as the
/// change they describe. The two commit together or neither does. Any design that writes the audit
/// afterwards — a second save, a queue, a different store — has a window in which the data moved and
/// the record of it did not, and that window is exactly where the disputed change will land.</para>
///
/// <para><b>Why <c>SavingChanges</c> and not <c>SavedChanges</c>.</b> The old values only exist
/// before the save. Afterwards every entry is <c>Unchanged</c>, the originals are gone, and a deleted
/// entity is no longer tracked at all — there would be nothing left to describe.</para>
/// </summary>
internal sealed class AuditSaveChangesInterceptor(
    IAuditContext auditContext,
    TimeProvider timeProvider) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        AddAuditEntries(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    /// <summary>
    /// The path everything in this application actually takes; the synchronous override above exists
    /// so that a future caller who saves synchronously is audited too rather than silently not.
    /// </summary>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AddAuditEntries(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AddAuditEntries(DbContext? dbContext)
    {
        if (dbContext is null)
        {
            return;
        }

        // The connection is configured with EnableRetryOnFailure, and a retried save re-enters this
        // hook with the same pending changes still sitting in the change tracker. Without this guard
        // a transient network blip would double every audit row for the affected request — a bug
        // that would never appear in a test and would only ever be seen as unexplained duplicates in
        // the one table nobody expects to lie.
        //
        // Untracked-after-success is what makes this safe rather than over-eager: once a save
        // succeeds its audit rows are Unchanged, not Added, so a genuine second unit of work in the
        // same scope is still audited.
        if (dbContext.ChangeTracker.Entries<AuditEntry>().Any(entry => entry.State == EntityState.Added))
        {
            return;
        }

        var occurredAt = timeProvider.GetUtcNow();
        var actor = auditContext.Actor;
        var correlationId = auditContext.CorrelationId;

        // Materialised before a single row is added. Adding to the change tracker while enumerating
        // it throws, and what is about to be added are tracked entities themselves.
        var auditEntries = dbContext.ChangeTracker.Entries()
            .Where(entry => entry.Entity is not AuditEntry)
            .Select(entry => Describe(entry, actor, correlationId, occurredAt))
            .OfType<AuditEntry>()
            .ToList();

        dbContext.AddRange(auditEntries);
    }

    private static AuditEntry? Describe(
        EntityEntry entry,
        string actor,
        string? correlationId,
        DateTimeOffset occurredAt)
    {
        var action = entry.State switch
        {
            EntityState.Added => AuditAction.Created,
            EntityState.Modified => AuditAction.Updated,
            EntityState.Deleted => AuditAction.Deleted,
            // Unchanged and Detached. A read produces neither, which is why GET traffic costs this
            // nothing: AsNoTracking reads do not populate the change tracker at all.
            _ => (AuditAction?)null,
        };

        if (action is null)
        {
            return null;
        }

        var changes = DescribeChanges(entry, action.Value);

        // An entity can be marked Modified while every value it holds is the one it already had —
        // an assignment of an identical value is enough. Recording that as a change would put rows
        // in the trail that say nothing happened, and the first person to query it would learn not
        // to trust the count.
        if (action is AuditAction.Updated && changes is null)
        {
            return null;
        }

        return new AuditEntry(
            Guid.CreateVersion7(),
            occurredAt,
            entry.Metadata.ClrType.Name,
            DescribeKey(entry),
            action.Value,
            actor,
            correlationId,
            changes);
    }

    /// <summary>
    /// The primary key of the row being described, as text.
    ///
    /// <para>Reading the key here — before the save — is only correct because every aggregate in this
    /// system assigns its own id: version-7 GUIDs from the domain factories, with
    /// <c>ValueGeneratedNever()</c> on every configuration. The value is therefore final at this
    /// point. Were a store-generated key ever introduced, its <c>CurrentValue</c> here would be a
    /// temporary placeholder EF Core replaces during the save, and this hook would have to record
    /// the entry and fill the id in <c>SavedChanges</c>. That is the one assumption in this class
    /// worth knowing before changing a key strategy.</para>
    /// </summary>
    private static string DescribeKey(EntityEntry entry)
    {
        var primaryKey = entry.Metadata.FindPrimaryKey();

        if (primaryKey is null)
        {
            return string.Empty;
        }

        return string.Join(
            ':',
            primaryKey.Properties.Select(property =>
                entry.Property(property.Name).CurrentValue?.ToString() ?? string.Empty));
    }

    private static string? DescribeChanges(EntityEntry entry, AuditAction action) => action switch
    {
        // Nothing: the created row is its own record. See AuditEntry.Changes for the rule.
        AuditAction.Created => null,

        AuditAction.Updated => DescribeModifiedProperties(entry),

        // Everything, because after this save there is nowhere else to read it from.
        AuditAction.Deleted => Serialize(entry.Properties.ToDictionary(
            property => property.Metadata.Name,
            property => ProviderValue(property, property.OriginalValue))),

        _ => null,
    };

    private static string? DescribeModifiedProperties(EntityEntry entry)
    {
        var modified = entry.Properties
            .Where(property => property.IsModified && !Equals(property.OriginalValue, property.CurrentValue))
            .ToDictionary(
                property => property.Metadata.Name,
                property => (object?)new
                {
                    old = ProviderValue(property, property.OriginalValue),
                    @new = ProviderValue(property, property.CurrentValue),
                });

        return modified.Count == 0 ? null : Serialize(modified);
    }

    /// <summary>
    /// The value as the database stores it, not as the CLR holds it.
    ///
    /// <c>Isbn</c>, <c>Barcode</c> and <c>MembershipNumber</c> are value objects behind converters,
    /// and serializing them raw would write <c>{"Value":"9780306406157"}</c> where the column
    /// contains <c>"9780306406157"</c>. An audit trail that does not match the table it audits is
    /// worse than none, because it is wrong in a way that looks right.
    /// </summary>
    private static object? ProviderValue(PropertyEntry property, object? value) =>
        property.Metadata.GetValueConverter() is { } converter && value is not null
            ? converter.ConvertToProvider(value)
            : value;

    private static string Serialize(Dictionary<string, object?> values) =>
        JsonSerializer.Serialize(values, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Property names come from the CLR model and are written as they are, so a column named
        // ReturnedAt reads as ReturnedAt here. Renaming them to camelCase would mean the audit trail
        // and the model disagreed about what a field is called, and the person reading this table is
        // usually holding the entity class open beside it.
        WriteIndented = false,
    };
}
