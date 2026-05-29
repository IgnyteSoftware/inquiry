using Inquiry.Generators.Abstractions;

namespace Inquiry.Generators.Models;

/// <summary>
/// Value-equatable replacement for the old <c>ColumnModel</c>. Still implements <see cref="IColumn"/>
/// so it feeds <c>SqlBuildContext</c> / <c>SqlBuilder</c> unchanged at emit time.
/// </summary>
internal sealed record ColumnData(
    string PropertyName,
    string ColumnName,
    TypeData Type,
    bool IsKey,
    bool IsGenerated,
    bool UseDatabaseDefault) : IColumn;
