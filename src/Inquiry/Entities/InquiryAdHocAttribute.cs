namespace Inquiry.Entities;

/// <summary>
/// Marks a plain DTO class or record for ad-hoc materialization. The source generator emits an
/// <c>IInquiryEntityMaterializer&lt;T&gt;</c> for the type and registers it via
/// <c>AddInquiryGeneratedStores()</c>, so the ad-hoc <see cref="IInquiry"/> query methods
/// (<c>QueryAsync&lt;T&gt;</c>, <c>QueryListAsync&lt;T&gt;</c>, <c>QuerySingleOrDefaultAsync&lt;T&gt;</c>)
/// can map hand-written reporting SQL into the type without it being an entity or a projection.
/// </summary>
/// <remarks>
/// Mapping is by name: every public or internal instance property with a public or internal setter
/// (<c>set</c> or <c>init</c>) reads the result column with the same name, compared without regard
/// to case. Alias SQL expressions to their property names; column order does not matter and extra
/// columns are ignored. A missing column, or two columns that match one property, throws
/// <see cref="InvalidOperationException"/> before the first row. Get-only (computed), static, and
/// privately-settable properties are skipped and need no column. The type must be
/// concrete with an accessible parameterless constructor (use init-only properties rather than
/// positional record parameters). No table, key, or column attributes are required;
/// <c>[InquiryEnumAsString]</c> is honored on enum properties.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class InquiryAdHocAttribute : Attribute
{
}
