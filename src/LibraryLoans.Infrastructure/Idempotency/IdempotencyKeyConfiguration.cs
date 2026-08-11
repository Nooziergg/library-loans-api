using LibraryLoans.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryLoans.Infrastructure.Idempotency;

/// <summary>
/// Maps the idempotency table. Mapped through the model rather than created by hand-written SQL so
/// that the migration and the snapshot know about it: a table that exists only in a
/// <c>migrationBuilder.Sql</c> call is invisible to every later diff.
/// </summary>
internal sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    /// <summary>
    /// Bounded because it is client-supplied. The middleware refuses anything longer before it ever
    /// reaches the database, and this is the same number so that the two cannot disagree.
    /// </summary>
    public const int KeyMaxLength = 128;

    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        builder.ToTable(IdempotencySchema.Table);

        // The key is the primary key, which is what makes the whole mechanism work: claiming a key
        // is an INSERT, and PostgreSQL decides which of two concurrent claims wins. No separate
        // surrogate id, because a second candidate key on a table whose entire purpose is one
        // uniqueness rule would be a place for the two to drift apart.
        builder.HasKey(entry => entry.Key)
            .HasName(DatabaseConstraints.IdempotencyKeysPrimaryKey);

        builder.Property(entry => entry.Key)
            .HasColumnName(IdempotencySchema.KeyColumn)
            .HasMaxLength(KeyMaxLength);

        builder.Property(entry => entry.Fingerprint)
            .HasColumnName(IdempotencySchema.FingerprintColumn)
            // SHA-256 as hex. Fixed width, so char(64) would do. Varchar keeps the option of
            // changing algorithm without a migration, at the cost of one length byte per row.
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(entry => entry.CreatedAt)
            .HasColumnName(IdempotencySchema.CreatedAtColumn)
            .IsRequired();

        // Null until the request finishes. That null is the "in progress" state.
        builder.Property(entry => entry.StatusCode)
            .HasColumnName(IdempotencySchema.StatusCodeColumn);

        builder.Property(entry => entry.ContentType)
            .HasColumnName(IdempotencySchema.ContentTypeColumn)
            .HasMaxLength(100);

        // jsonb, like the audit trail's changes column and for the same reasons: validated on write,
        // and readable in psql without a client to parse it.
        builder.Property(entry => entry.Headers)
            .HasColumnName(IdempotencySchema.HeadersColumn)
            .HasColumnType("jsonb");

        builder.Property(entry => entry.Body)
            .HasColumnName(IdempotencySchema.BodyColumn);

        // What a retention job deletes on. Building the index now costs nothing on a small table and
        // means the job that eventually runs is not a sequential scan over everything ever retried.
        builder.HasIndex(entry => entry.CreatedAt)
            .HasDatabaseName(DatabaseConstraints.IdempotencyKeysCreatedAtIndex);
    }
}
