using System.Collections.Generic;

namespace Inquiry.IntegrationTesting;

public sealed record ColumnSnapshot(string Name, bool IsNullable);

public sealed record ForeignKeySnapshot(
    IReadOnlyList<string> Columns,
    string ReferencedTable,
    IReadOnlyList<string> ReferencedColumns);

/// <summary>An index described by its ordered column list; matched name-agnostically.</summary>
public sealed record IndexSnapshot(IReadOnlyList<string> Columns);

public sealed record TableSnapshot(
    string Name,
    IReadOnlyList<ColumnSnapshot> Columns,
    IReadOnlyList<string> PrimaryKey,
    IReadOnlyList<ForeignKeySnapshot> ForeignKeys,
    IReadOnlyList<IndexSnapshot> Indexes);

public sealed record SchemaSnapshot(IReadOnlyList<TableSnapshot> Tables);
