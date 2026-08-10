namespace LibraryLoans.IntegrationTests.Infrastructure;

/// <summary>
/// Shares one container across every integration test class. Starting a PostgreSQL per class
/// would multiply the suite's runtime by the number of classes for no isolation benefit that
/// truncating between classes does not already provide.
/// </summary>
[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
