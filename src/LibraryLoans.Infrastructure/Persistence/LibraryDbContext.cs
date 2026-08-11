using LibraryLoans.Domain.Books;
using LibraryLoans.Domain.Copies;
using LibraryLoans.Domain.Loans;
using LibraryLoans.Domain.Members;
using Microsoft.EntityFrameworkCore;

namespace LibraryLoans.Infrastructure.Persistence;

/// <summary>
/// The EF Core model for the library.
///
/// This type is internal to the persistence story: handlers depend on the ports in the
/// Application layer, never on the context, so a change of ORM would rewrite this folder and
/// nothing above it.
/// </summary>
public sealed class LibraryDbContext(DbContextOptions<LibraryDbContext> options) : DbContext(options)
{
    public DbSet<Book> Books => Set<Book>();

    public DbSet<BookCopy> BookCopies => Set<BookCopy>();

    public DbSet<Member> Members => Set<Member>();

    public DbSet<Loan> Loans => Set<Loan>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Required by the trigram indexes on the catalogue's searchable columns. Declared here so
        // it is part of the model, which means the migration creates it and the snapshot records
        // it. Note for anyone deploying elsewhere: CREATE EXTENSION needs a role with rights to it.
        // Managed PostgreSQL grants pg_trgm to its admin role, but a locked-down application role
        // would fail this migration at startup: one more reason production applies migrations as a
        // separate deployment step rather than on boot.
        modelBuilder.HasPostgresExtension("pg_trgm");

        // Configuration classes are discovered from this assembly, so adding an aggregate means
        // adding one file rather than also remembering to register it here.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LibraryDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
