using Inquiry.Generators.Infrastructure;

namespace Inquiry.Generators.Models;

/// <summary>The shape a <c>[InquiryStoredProcedure]</c> method returns, decided at discovery.</summary>
internal enum ProcedureReturnKind
{
    None,
    AsyncEnumerableOfEntity,
    TaskOfEntity,
    TaskOfInt,

    /// <summary>
    /// <c>Task&lt;TScalar&gt;</c> surfacing a single OUTPUT parameter or the RETURN value as the task
    /// result (driven by <c>[InquiryStoredProcedure(OutputParameter=…/ReturnsValue=true)]</c>).
    /// </summary>
    TaskOfOutputScalar,
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
    /// For a select operation, whether <c>IncludeDeleted = true</c> was set so the soft-delete
    /// active filter is suppressed. Ignored for entities without a soft-delete column.
    /// </summary>
    public bool IncludeDeleted { get; init; }

    /// <summary>
    /// For a select operation, whether <c>Distinct = true</c> was set so the generated query
    /// emits <c>SELECT DISTINCT</c> instead of <c>SELECT</c>.
    /// </summary>
    public bool Distinct { get; init; }

    /// <summary>
    /// For <see cref="StoreOperation.DeleteOneByKey"/>, whether <c>HardDelete = true</c> was set so
    /// a literal <c>DELETE</c> is emitted even when the entity declares a soft-delete column.
    /// </summary>
    public bool HardDelete { get; init; }

    /// <summary>For <see cref="StoreOperation.Aggregate"/>, the SQL function (SUM/AVG/MIN/MAX).</summary>
    public string? AggregateFunction { get; init; }

    /// <summary>For <see cref="StoreOperation.Aggregate"/>, the raw column name (resolved at emit).</summary>
    public string? AggregateColumn { get; init; }

    /// <summary>
    /// For <see cref="StoreOperation.Aggregate"/>/<see cref="StoreOperation.Count"/>, the scalar
    /// result type the method returns (the <c>T</c> in <c>Task&lt;T&gt;</c>), passed to
    /// <c>ExecuteScalarAsync&lt;T&gt;</c>.
    /// </summary>
    public string? ScalarResultType { get; init; }

    /// <summary>
    /// For a select-list operation, the fully-qualified element type the method returns (the
    /// <c>T</c> in <c>Task&lt;IReadOnlyList&lt;T&gt;&gt;</c> / <c>IAsyncEnumerable&lt;T&gt;</c>). Equal to
    /// the store's entity for an ordinary select; a different type is resolved against the projection
    /// registry at emit. Null for non-select operations.
    /// </summary>
    public string? ResultElementTypeFqn { get; init; }

    /// <summary>
    /// For <see cref="ProcedureReturnKind.TaskOfOutputScalar"/>, the normalized parameter name read
    /// back after execution (e.g. <c>@Total</c> for an OUTPUT parameter, or the synthetic
    /// return-value name). Passed to <c>IInquiry.ExecuteProcedureScalarAsync</c>.
    /// </summary>
    public string? ProcedureReadBackName { get; init; }

    /// <summary>
    /// For <see cref="ProcedureReturnKind.TaskOfOutputScalar"/>, whether the read-back parameter is
    /// the procedure's RETURN value (<c>ParameterDirection.ReturnValue</c>) rather than an OUTPUT
    /// parameter (<c>ParameterDirection.Output</c>).
    /// </summary>
    public bool ProcedureReturnsValue { get; init; }

    /// <summary>
    /// For an OUTPUT-parameter <see cref="ProcedureReturnKind.TaskOfOutputScalar"/>, the DbType
    /// enum-member expression to stamp on the output parameter so the provider allocates the right
    /// read-back buffer; null when no portable DbType applies (RETURN value, or unmapped type).
    /// </summary>
    public string? ProcedureOutputDbType { get; init; }

    /// <summary>
    /// For an OUTPUT-parameter scalar that maps to a variable-length string, whether to stamp
    /// <c>Size = -1</c> (MAX) so providers like SqlClient allocate an output buffer.
    /// </summary>
    public bool ProcedureOutputIsString { get; init; }

    /// <summary>
    /// For a <see cref="decimal"/> OUTPUT parameter, whether to stamp an explicit precision/scale.
    /// SqlClient defaults a decimal output parameter to scale 0 and rounds the read-back value
    /// (e.g. 19.75 → 20), so a high-fidelity scale is stamped to preserve fractional digits.
    /// </summary>
    public bool ProcedureOutputIsDecimal { get; init; }
}
