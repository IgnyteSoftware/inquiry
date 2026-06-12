namespace Inquiry.Sqlite.Tests.Fixtures;

/// <summary>
/// Per-feature DDL strings for fixtures that exercise specific Inquiry behaviors
/// (generated keys, database-default columns) that the shared Northwind schema does
/// not naturally cover. Tests against the full Northwind schema pass
/// <c>Inquiry.Northwind.NorthwindSchema.SqliteDdl</c> directly.
/// </summary>
internal static class Schemas
{
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

    public const string ScheduleItem = """
        CREATE TABLE TScheduleItem (
            Id INTEGER PRIMARY KEY,
            EventDate TEXT NOT NULL,
            StartTime TEXT NOT NULL,
            EndDate TEXT,
            EndTime TEXT
        );
        """;

    public const string DefaultedKeyItem = """
        CREATE TABLE TDefaultedKeyItem (
            Id TEXT PRIMARY KEY DEFAULT (lower(hex(randomblob(16)))),
            Name TEXT NOT NULL
        );
        """;
}
