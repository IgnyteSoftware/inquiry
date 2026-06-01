using Inquiry.Generators.Infrastructure;

namespace Inquiry.Generators.Models;

/// <summary>The shape a <c>[InquiryStoredProcedure]</c> method returns, decided at discovery.</summary>
internal enum ProcedureReturnKind
{
    None,
    AsyncEnumerableOfEntity,
    TaskOfEntity,
    TaskOfInt,
}

/// <summary>
/// Value-equatable replacement for the old <c>StoreMethodModel</c>. Carries the store-method facts
/// extracted in the discovery transform. <see cref="FieldNames"/> holds the raw
/// <c>[InquirySelectAllByField]</c> argument names; they are resolved against the entity's columns
/// (and validated) in the combined emit stage, because that resolution depends on the entity model.
/// <see cref="Predicates"/> holds the <c>[InquiryWhere]</c> criteria in source order for the
/// <c>SelectAllByPredicate</c> operation (resolved/validated the same way).
/// </summary>
internal sealed record StoreMethodData(
    string Name,
    StoreOperation Operation,
    string ReturnTypeDisplay,
    EquatableArray<ParameterData> Parameters,
    EquatableArray<string> FieldNames,
    EquatableArray<PredicateData> Predicates,
    string? ProcedureName,
    bool ReturnsEntity,
    bool ReturnsList,
    ProcedureReturnKind ProcedureReturn,
    LocationData? Location)
{
    /// <summary>
    /// Parsed ORDER BY terms (from <c>OrderBy = "…"</c> on a select attribute, or the keyset key fields),
    /// in significance order. Empty when no ordering was requested. Fields are resolved/quoted at emit.
    /// </summary>
    public EquatableArray<OrderItem> OrderBy { get; init; } = EquatableArray<OrderItem>.Empty;

    /// <summary>The pagination mode requested on this method.</summary>
    public Pagination Pagination { get; init; } = Pagination.None;

    /// <summary>
    /// For <see cref="StoreOperation.KeysetPage"/>, the raw keyset field names (most-significant first),
    /// resolved against the entity's columns at emit.
    /// </summary>
    public EquatableArray<string> KeysetFields { get; init; } = EquatableArray<string>.Empty;

    /// <summary>For <see cref="StoreOperation.KeysetPage"/>, whether the keyset walks descending (Backward).</summary>
    public bool KeysetDescending { get; init; }

    /// <summary>
    /// W8: for a select operation, whether <c>IncludeDeleted = true</c> was set so the soft-delete
    /// active filter is suppressed. Ignored for entities without a soft-delete column.
    /// </summary>
    public bool IncludeDeleted { get; init; }

    /// <summary>
    /// W8: for <see cref="StoreOperation.DeleteOneByKey"/>, whether <c>HardDelete = true</c> was set so
    /// a literal <c>DELETE</c> is emitted even when the entity declares a soft-delete column.
    /// </summary>
    public bool HardDelete { get; init; }

    /// <summary>W5: for <see cref="StoreOperation.Aggregate"/>, the SQL function (SUM/AVG/MIN/MAX).</summary>
    public string? AggregateFunction { get; init; }

    /// <summary>W5: for <see cref="StoreOperation.Aggregate"/>, the raw column name (resolved at emit).</summary>
    public string? AggregateColumn { get; init; }

    /// <summary>
    /// W5: for <see cref="StoreOperation.Aggregate"/>/<see cref="StoreOperation.Count"/>, the scalar
    /// result type the method returns (the <c>T</c> in <c>Task&lt;T&gt;</c>), passed to
    /// <c>ExecuteScalarAsync&lt;T&gt;</c>.
    /// </summary>
    public string? ScalarResultType { get; init; }
}
