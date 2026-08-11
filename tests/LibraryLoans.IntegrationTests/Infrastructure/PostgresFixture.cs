using Npgsql;
using Testcontainers.PostgreSql;

namespace LibraryLoans.IntegrationTests.Infrastructure;

/// <summary>
/// A real PostgreSQL, started for the test run and destroyed afterwards.
///
/// This is how the suite satisfies the requirement that tests never touch the development
/// database: the connection string does not exist until the container starts, so there is
/// nothing to accidentally point at a real server, and no developer has to remember to reset
/// anything. <c>dotnet test</c> is the whole procedure.
///
/// A real database rather than an in-memory substitute is the point. The behaviour under test
/// includes a unique index deciding a race and PostgreSQL reporting SQLSTATE 23505 with a
/// constraint name: none of which an in-memory provider reproduces, so a suite built on one
/// would pass while the production path was broken.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // Pinned to the same major version compose runs, so the tests exercise the engine the
    // application is actually deployed against.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("library_tests")
        .WithUsername("library")
        .WithPassword("library_tests_only")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>
    /// Empties every application table between test classes.
    ///
    /// The table list is read from the catalogue rather than hardcoded, so adding an aggregate
    /// in a later phase does not silently leave a table uncleared: the failure that produces is
    /// an unrelated test failing intermittently, which is expensive to diagnose.
    ///
    /// The migrations history table is excluded: clearing it would make EF Core believe the
    /// schema had never been created.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            DO $$
            DECLARE truncate_statement text;
            BEGIN
                SELECT 'TRUNCATE TABLE '
                       || string_agg(format('%I.%I', schemaname, tablename), ', ')
                       || ' CASCADE'
                INTO truncate_statement
                FROM pg_tables
                WHERE schemaname = 'public'
                  AND tablename <> '__EFMigrationsHistory';

                IF truncate_statement IS NOT NULL THEN
                    EXECUTE truncate_statement;
                END IF;
            END $$;
            """;

        await command.ExecuteNonQueryAsync();
    }
}
