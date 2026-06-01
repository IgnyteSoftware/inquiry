namespace Inquiry.Generators.Abstractions;

/// <summary>
/// The soft-delete representation a column carries (W8). <see cref="None"/> for ordinary columns;
/// <see cref="BooleanFlag"/> for a <c>bool</c> indicator (active = <c>0</c>); <see cref="Timestamp"/>
/// for a nullable <c>DateTime</c>/<c>DateTimeOffset</c> indicator (active = <c>NULL</c>).
/// </summary>
public enum SoftDeleteKind
{
    None,
    BooleanFlag,
    Timestamp,
}

/// <summary>
/// Minimal column contract consumed by <see cref="SqlBuilder"/> implementations. Provider analyzers
/// only see this surface; the generator's richer internal column model implements it.
/// </summary>
public interface IColumn
{
    string PropertyName { get; }
    string ColumnName { get; }
    bool IsKey { get; }
    bool IsGenerated { get; }
    bool UseDatabaseDefault { get; }

    /// <summary>The soft-delete role this column plays (W8); <see cref="SoftDeleteKind.None"/> for most.</summary>
    SoftDeleteKind SoftDelete { get; }
}
