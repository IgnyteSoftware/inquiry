using Inquiry.Generators.Infrastructure;

namespace Inquiry.Generators.Models;

internal sealed record IndexData(
    string? Schema,
    string Table,
    EquatableArray<string> KeyColumns,
    EquatableArray<string> IncludeColumns,
    bool IsUnique,
    string? RequestedName,
    LocationData? Location)
{
    public EquatableArray<string> LogicalKeyProperties { get; init; }
    public EquatableArray<string> LogicalIncludeProperties { get; init; }
    public string CanonicalIdentity { get; init; } = string.Empty;
    public string? EmittedName { get; init; }
    public IndexOrigin Origin { get; init; }
    public int Ordinal { get; init; }
}

internal enum IndexOrigin { ColumnFlag, TableAttribute }
