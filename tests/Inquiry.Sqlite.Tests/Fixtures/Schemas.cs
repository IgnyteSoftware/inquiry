namespace Inquiry.Sqlite.Tests.Fixtures;

/// <summary>
/// Single source of truth for DDL used by integration tests. Keeping these
/// in one place prevents schema drift between tests that share fixtures.
/// </summary>
internal static class Schemas
{
    public const string Organization = """
        CREATE TABLE TOrganization (
            [Key] TEXT PRIMARY KEY,
            [Name] TEXT NOT NULL,
            IsActive INTEGER DEFAULT 1 NOT NULL
        );
        """;

    public const string Product = """
        CREATE TABLE TProduct (
            Key TEXT PRIMARY KEY,
            Name TEXT NOT NULL,
            Price REAL NOT NULL,
            CategoryKey TEXT NOT NULL
        );
        """;

    public const string Category = """
        CREATE TABLE TCategory (
            Key TEXT PRIMARY KEY,
            Name TEXT NOT NULL
        );
        """;

    /// <summary>Both category and product tables in one batch.</summary>
    public const string CategoryAndProduct = Category + "\n" + Product;
}
