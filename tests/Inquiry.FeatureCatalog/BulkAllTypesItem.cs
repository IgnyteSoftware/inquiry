using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

public enum BulkColor { Red = 0, Green = 1, Blue = 2 }

/// <summary>
/// Fixture for <c>[InquiryBulkInsert]</c> all-types coverage (#134): a single entity with one column
/// per provider-primitive category (int, decimal, bool, Guid, DateTime, string, byte[], enum,
/// converter). The bulk-insert test round-trips a single row to verify each type survives the
/// provider's bulk-copy serialization (native copier on PG/SS/MySQL/MariaDB, batch-INSERT fallback
/// on SQLite/Oracle).
/// </summary>
[InquiryTable("BulkAllTypesItem")]
public sealed class BulkAllTypesItem
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn]
    public int IntVal { get; set; }

    [InquiryColumn(Precision = 18, Scale = 2)]
    public decimal DecimalVal { get; set; }

    [InquiryColumn]
    public bool BoolVal { get; set; }

    [InquiryColumn]
    public Guid GuidVal { get; set; }

    [InquiryColumn]
    public DateTime DateTimeVal { get; set; }

    [InquiryColumn(Length = 200)]
    public string StringVal { get; set; } = string.Empty;

    [InquiryColumn(Length = 200)]
    public string? NullableStringVal { get; set; }

    [InquiryColumn]
    public byte[] BinaryVal { get; set; } = Array.Empty<byte>();

    [InquiryColumn]
    public BulkColor EnumVal { get; set; }

    [InquiryColumn("ConvertedVal", Converter = typeof(MoneyConverter))]
    public Money ConvertedVal { get; set; }
}

public partial class BulkAllTypesItemStore : InquiryStore<BulkAllTypesItem>
{
    [InquiryBulkInsert]
    public partial Task<long> BulkInsertAsync(IEnumerable<BulkAllTypesItem> items, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<BulkAllTypesItem?> GetAsync(int id, CancellationToken cancellationToken = default);
}
