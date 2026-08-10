using LibraryLoans.IntegrationTests.Infrastructure;
using Npgsql;

namespace LibraryLoans.IntegrationTests.Loans;

/// <summary>
/// Asserts directly against the catalogue that the index carrying the loan invariant is
/// <b>partial</b>, and not a plain unique index.
///
/// This is a belt to the braces of
/// <c>LoansEndpointsTests.Borrows_the_same_copy_again_after_it_has_been_returned</c>, which is the
/// behavioural proof. Both exist because the difference between the two indexes is invisible to
/// every other test in the suite while being the difference between "a copy may have one active
/// loan" and "a copy may be borrowed once, ever". This one states the requirement in the same
/// language the database does, so a reader does not have to derive it from behaviour.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ActiveLoanIndexTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private LibraryApiFactory _factory = null!;

    public ActiveLoanIndexTests(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        await _postgres.ResetAsync();
        _factory = new LibraryApiFactory(_postgres.ConnectionString);
        // Forces the host to build and apply migrations before the catalogue is inspected.
        _ = _factory.Services;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task The_active_loan_index_is_unique_and_filtered_on_unreturned_loans()
    {
        var definition = await IndexDefinitionAsync("ix_loans_active_copy");

        Assert.NotNull(definition);
        Assert.Contains("UNIQUE", definition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("book_copy_id", definition, StringComparison.Ordinal);

        // The clause that turns "one loan per copy, ever" into "one *active* loan per copy".
        Assert.Contains("returned_at IS NULL", definition, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_member_active_loan_index_is_filtered_but_not_unique()
    {
        var definition = await IndexDefinitionAsync("ix_loans_member_active");

        Assert.NotNull(definition);
        // Not unique: a member may hold several loans at once, up to the policy limit. This index
        // exists so counting them is a seek rather than a scan over the whole loan history.
        Assert.DoesNotContain("UNIQUE", definition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("returned_at IS NULL", definition, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string?> IndexDefinitionAsync(string indexName)
    {
        await using var connection = new NpgsqlConnection(_postgres.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT indexdef FROM pg_indexes WHERE indexname = @name";
        command.Parameters.AddWithValue("name", indexName);

        return await command.ExecuteScalarAsync() as string;
    }
}
