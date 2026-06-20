using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

// ---------------------------------------------------------------------------
// Repro fixture for bugs #48 (read) and #49 (write) with unsigned / sbyte
// CLR types. Each property exercises one problematic type.
//
// SQL column mapping (from EntityProcessor + SqlBuilder):
//   sbyte   → DbTypeClass.Byte  → TINYINT  / INTEGER
//   ushort  → DbTypeClass.Int16 → SMALLINT / INTEGER
//   uint    → DbTypeClass.Int32 → INT      / INTEGER
//   ulong   → DbTypeClass.Int64 → BIGINT   / INTEGER
//
// Bug #49: DbTypeMapper previously emitted DbType.SByte / UInt16 / UInt32 / UInt64
//          on the parameter, which SqlClient rejects at bind time.
// Bug #48: MaterializerEmitter previously fell to GetFieldValue<T> for these types;
//          SqlClient stores the value as the signed equivalent and the
//          unbox to the unsigned type threw InvalidCastException.
// ---------------------------------------------------------------------------

/// <summary>
/// Plain integer types — covers every CLR type that is unsigned or signed-but-problematic.
/// Key is a plain <c>int</c> so the key binding is never the trigger.
/// </summary>
[InquiryTable("UnsignedTypesItem")]
public sealed class UnsignedTypesItem
{
    [InquiryKey("Id")]
    public int Id { get; set; }

    // Plain signed — these should work fine and are here as control cases.
    [InquiryColumn("ByteVal")]
    public byte ByteVal { get; set; }

    [InquiryColumn("Int16Val")]
    public short Int16Val { get; set; }

    [InquiryColumn("Int32Val")]
    public int Int32Val { get; set; }

    [InquiryColumn("Int64Val")]
    public long Int64Val { get; set; }

    // Problematic unsigned / sbyte types.
    [InquiryColumn("SByteVal")]
    public sbyte SByteVal { get; set; }

    [InquiryColumn("UInt16Val")]
    public ushort UInt16Val { get; set; }

    [InquiryColumn("UInt32Val")]
    public uint UInt32Val { get; set; }

    [InquiryColumn("UInt64Val")]
    public ulong UInt64Val { get; set; }

    // Enum columns — one per problematic underlying type.
    [InquiryColumn("EnumInt32Val")]
    public SampleEnumInt32 EnumInt32Val { get; set; }

    [InquiryColumn("EnumSByteVal")]
    public SampleEnumSByte EnumSByteVal { get; set; }

    [InquiryColumn("EnumUInt16Val")]
    public SampleEnumUInt16 EnumUInt16Val { get; set; }

    [InquiryColumn("EnumUInt32Val")]
    public SampleEnumUInt32 EnumUInt32Val { get; set; }

    [InquiryColumn("EnumUInt64Val")]
    public SampleEnumUInt64 EnumUInt64Val { get; set; }
}

/// <summary>Control: int-backed enum (should work fine).</summary>
public enum SampleEnumInt32 : int
{
    Zero = 0,
    One = 1,
    MaxSigned = int.MaxValue,
}

/// <summary>sbyte-backed enum — includes a negative member.</summary>
public enum SampleEnumSByte : sbyte
{
    Negative = -1,
    Zero = 0,
    Max = sbyte.MaxValue,
}

/// <summary>ushort-backed enum — includes a member beyond short.MaxValue.</summary>
public enum SampleEnumUInt16 : ushort
{
    Zero = 0,
    AboveShortMax = 40000,
    Max = ushort.MaxValue,
}

/// <summary>uint-backed enum — includes a member beyond int.MaxValue.</summary>
public enum SampleEnumUInt32 : uint
{
    Zero = 0,
    AboveIntMax = 3_000_000_000u,
    Max = uint.MaxValue,
}

/// <summary>
/// ulong-backed enum. The reinterpret binding stores values beyond long.MaxValue as their
/// signed bit pattern (e.g. <see cref="AboveLongMax"/> → a negative long), so they fit BIGINT
/// and round-trip back exactly.
/// </summary>
public enum SampleEnumUInt64 : ulong
{
    Zero = 0,
    Large = 9_000_000_000_000_000_000ul,        // < long.MaxValue
    AboveLongMax = 18_000_000_000_000_000_000ul, // > long.MaxValue (9.22e18)
    Max = ulong.MaxValue,
}

public partial class UnsignedTypesItemStore : InquiryStore<UnsignedTypesItem>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(UnsignedTypesItem item, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<UnsignedTypesItem?> SelectByKeyAsync(int id, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<UnsignedTypesItem>> SelectAllAsync(CancellationToken cancellationToken = default);
}
