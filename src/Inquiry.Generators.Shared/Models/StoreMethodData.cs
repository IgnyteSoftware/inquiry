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
/// </summary>
internal sealed record StoreMethodData(
    string Name,
    StoreOperation Operation,
    string ReturnTypeDisplay,
    EquatableArray<ParameterData> Parameters,
    EquatableArray<string> FieldNames,
    string? ProcedureName,
    bool ReturnsEntity,
    bool ReturnsList,
    ProcedureReturnKind ProcedureReturn,
    LocationData? Location);
