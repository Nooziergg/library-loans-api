using LibraryLoans.Domain.Books;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configuration classes are discovered from this assembly, so adding an aggregate means
        // adding one file rather than also remembering to register it here.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LibraryDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
