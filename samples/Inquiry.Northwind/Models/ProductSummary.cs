using Inquiry.Entities;

namespace Inquiry.Northwind.Models;

/// <summary>
/// Projection over <see cref="Product"/>: a column subset materialized by ordinal. Selecting this
/// instead of the full entity emits only the declared columns.
/// </summary>
[InquiryProjection(typeof(Product))]
public sealed record ProductSummary
{
    [InquiryColumn("ProductID")]
    public int? ProductID { get; init; }

    [InquiryColumn("ProductName")]
    public string ProductName { get; init; } = string.Empty;

    [InquiryColumn("UnitPrice")]
    public decimal? UnitPrice { get; init; }
}
