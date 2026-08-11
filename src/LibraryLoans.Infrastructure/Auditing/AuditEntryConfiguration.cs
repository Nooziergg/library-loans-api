using LibraryLoans.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryLoans.Infrastructure.Auditing;

/// <summary>
/// Maps the audit trail. Discovered by <c>ApplyConfigurationsFromAssembly</c> like every other
/// configuration, which is why <see cref="AuditEntry"/> needs no <c>DbSet</c> to be part of the
/// model.
/// </summary>
internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");

        builder.HasKey(entry => entry.Id)
            .HasName(DatabaseConstraints.AuditEntriesPrimaryKey);

        builder.Property(entry => entry.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(entry => entry.OccurredAt)
            .HasColumnName("occurred_at")
            .IsRequired();

        builder.Property(entry => entry.EntityType)
            .HasColumnName("entity_type")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(entry => entry.EntityId)
            .HasColumnName("entity_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(entry => entry.Action)
            .HasColumnName("action")
            .HasMaxLength(20)
            // As text. An audit table is read by people, and an integer would need a lookup nobody
            // has written down.
            .HasConversion<string>()
            .IsRequired();

        builder.Property(entry => entry.Actor)
            .HasColumnName("actor")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(entry => entry.CorrelationId)
            .HasColumnName("correlation_id")
            // The same bound the middleware enforces on an inbound header, so a value that passed
            // that check cannot be rejected by this column.
            .HasMaxLength(64);

        builder.Property(entry => entry.Changes)
            .HasColumnName("changes")
            // jsonb rather than text: it is validated on write, so a malformed document cannot be
            // stored, and it can be queried with the -> operators without parsing it in the client.
            // The cost is a slightly slower insert, on rows nobody is in a hurry to write.
            .HasColumnType("jsonb");

        // "Everything that ever happened to this loan" — the question an audit trail is opened to
        // answer, and a sequential scan over a table that only grows is not an answer.
        builder.HasIndex(entry => new { entry.EntityType, entry.EntityId })
            .HasDatabaseName(DatabaseConstraints.AuditEntriesEntityIndex);

        // "What happened between 14:00 and 14:05", which is the other question, and the one a
        // retention job also needs when it deletes everything older than a cutoff.
        builder.HasIndex(entry => entry.OccurredAt)
            .HasDatabaseName(DatabaseConstraints.AuditEntriesOccurredAtIndex);
    }
}
