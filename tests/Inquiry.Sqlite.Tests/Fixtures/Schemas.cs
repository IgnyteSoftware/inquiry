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

    public const string GeneratedItem = """
        CREATE TABLE TGeneratedItem (
            Id INTEGER PRIMARY KEY,
            Name TEXT NOT NULL
        );
        """;

    public const string DefaultedItem = """
        CREATE TABLE TDefaultedItem (
            Key TEXT PRIMARY KEY,
            Name TEXT NOT NULL,
            Status TEXT DEFAULT 'New' NOT NULL
        );
        """;

    public const string DefaultedKeyItem = """
        CREATE TABLE TDefaultedKeyItem (
            Id TEXT PRIMARY KEY DEFAULT (lower(hex(randomblob(16)))),
            Name TEXT NOT NULL
        );
        """;

    /// <summary>Both category and product tables in one batch.</summary>
    public const string CategoryAndProduct = Category + "\n" + Product;
}
