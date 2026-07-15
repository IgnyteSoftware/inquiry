using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

public readonly record struct UnsignedCollectionCode(uint Value);
public sealed class UnsignedCollectionCodeConverter : IInquiryValueConverter<UnsignedCollectionCode, uint>
{
    public uint ToProvider(UnsignedCollectionCode value) => value.Value;
    public UnsignedCollectionCode FromProvider(uint value) => new(value);
}
public enum UnsignedCollectionState : uint { High = 3_000_000_000u, Max = uint.MaxValue }

[InquiryTable("UnsignedCollectionItem")]
public sealed class UnsignedCollectionItem
{
    [InquiryKey] public uint Id { get; set; }
    [InquiryColumn] public sbyte S8 { get; set; }
    [InquiryColumn] public ushort U16 { get; set; }
    [InquiryColumn] public uint U32 { get; set; }
    [InquiryColumn] public ulong U64 { get; set; }
    [InquiryColumn(Converter = typeof(UnsignedCollectionCodeConverter))] public UnsignedCollectionCode Code { get; set; }
    [InquiryColumn] public UnsignedCollectionState State { get; set; }
}

public partial class UnsignedCollectionItemStore : InquiryStore<UnsignedCollectionItem>
{
    [InquiryInsert] public partial Task<int> InsertAsync(UnsignedCollectionItem item, CancellationToken ct = default);
    [InquirySelectAllByPredicate, InquiryWhere("S8", Compare.In)] public partial Task<IReadOnlyList<UnsignedCollectionItem>> ByS8Async(IReadOnlyList<sbyte> values, CancellationToken ct = default);
    [InquirySelectAllByPredicate, InquiryWhere("U16", Compare.In)] public partial Task<IReadOnlyList<UnsignedCollectionItem>> ByU16Async(IReadOnlyList<ushort> values, CancellationToken ct = default);
    [InquirySelectAllByPredicate, InquiryWhere("U32", Compare.In)] public partial Task<IReadOnlyList<UnsignedCollectionItem>> ByU32Async(IReadOnlyList<uint> values, CancellationToken ct = default);
    [InquirySelectAllByPredicate, InquiryWhere("U64", Compare.In)] public partial Task<IReadOnlyList<UnsignedCollectionItem>> ByU64Async(IReadOnlyList<ulong> values, CancellationToken ct = default);
    [InquirySelectAllByPredicate, InquiryWhere("Code", Compare.In)] public partial Task<IReadOnlyList<UnsignedCollectionItem>> ByCodeAsync(IReadOnlyList<UnsignedCollectionCode> values, CancellationToken ct = default);
    [InquirySelectAllByPredicate, InquiryWhere("State", Compare.In)] public partial Task<IReadOnlyList<UnsignedCollectionItem>> ByStateAsync(IReadOnlyList<UnsignedCollectionState> values, CancellationToken ct = default);
    [InquirySelectAllByPredicate, InquiryWhere("U32", Compare.NotIn)] public partial Task<IReadOnlyList<UnsignedCollectionItem>> NotU32Async(IReadOnlyList<uint> values, CancellationToken ct = default);
    [InquiryDeleteAll] public partial Task<int> DeleteAllAsync(IReadOnlyList<uint> ids, CancellationToken ct = default);
}

[InquiryTable("UnsignedConverterKey")]
public sealed class UnsignedConverterKey
{
    [InquiryKey(Converter = typeof(UnsignedCollectionCodeConverter))] public UnsignedCollectionCode Id { get; set; }
}
public partial class UnsignedConverterKeyStore : InquiryStore<UnsignedConverterKey>
{
    [InquiryInsert] public partial Task<int> InsertAsync(UnsignedConverterKey item, CancellationToken ct = default);
    [InquiryDeleteAll] public partial Task<int> DeleteAllAsync(IReadOnlyList<UnsignedCollectionCode> ids, CancellationToken ct = default);
}

[InquiryTable("UnsignedEnumKey")]
public sealed class UnsignedEnumKey
{
    [InquiryKey] public UnsignedCollectionState Id { get; set; }
}
public partial class UnsignedEnumKeyStore : InquiryStore<UnsignedEnumKey>
{
    [InquiryInsert] public partial Task<int> InsertAsync(UnsignedEnumKey item, CancellationToken ct = default);
    [InquiryDeleteAll] public partial Task<int> DeleteAllAsync(IReadOnlyList<UnsignedCollectionState> ids, CancellationToken ct = default);
}
