using System;

namespace Inquiry.Entities;

/// <summary>
/// Marks a plain DTO class or record for ad-hoc materialization. The source generator emits an
/// <c>IInquiryEntityMaterializer&lt;T&gt;</c> for the type and registers it via
/// <c>AddInquiryGeneratedStores()</c>, so the ad-hoc <see cref="IInquiry"/> query methods
/// (<c>QueryAsync&lt;T&gt;</c>, <c>QueryListAsync&lt;T&gt;</c>, <c>QuerySingleOrDefaultAsync&lt;T&gt;</c>)
/// can map hand-written reporting SQL into the type without it being an entity or a projection.
/// </summary>
/// <remarks>
/// Mapping is by ordinal: every public or internal instance property with a public or internal
/// setter (<c>set</c> or <c>init</c>) maps to one SELECT-list position, in declaration order — the
/// query's SELECT list must therefore match the property order. Get-only (computed), static, and
/// privately-settable properties are skipped and do not occupy an ordinal. The type must be
/// concrete with an accessible parameterless constructor (use init-only properties rather than
/// positional record parameters). No table, key, or column attributes are required;
/// <c>[InquiryEnumAsString]</c> is honored on enum properties.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class InquiryAdHocAttribute : Attribute
{
}
