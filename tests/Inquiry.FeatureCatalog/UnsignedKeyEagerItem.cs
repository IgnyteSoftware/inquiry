using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

// ---------------------------------------------------------------------------
// Repro fixture for the eager-load sibling of bug #49: a uint KEY whose value
// exceeds int.MaxValue. The eager-load emitter binds the key/FK through
// new InquiryParameter(name, rawValue, DbType.Int32) — the RAW boxed uint plus
// the now-signed DbType. Without the runtime binder reinterpreting the value,
// SqlClient does a CHECKED Convert.ToInt32(uint) and throws OverflowException.
//
// Parent key is CLIENT-SUPPLIED (not generated) so the test can insert a value
// above int.MaxValue (e.g. 3_000_000_000). The child carries a uint FK back to
// the parent key; the [InquiryRelation] collection is populated by the eager load.
// ---------------------------------------------------------------------------

/// <summary>Parent with a client-supplied <c>uint</c> key above int.MaxValue and a child collection.</summary>
[InquiryTable("UnsignedKeyParent")]
public sealed class UnsignedKeyParent
{
    [InquiryKey("Id")]
    public uint Id { get; set; }

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;

    [InquiryRelation(nameof(UnsignedKeyChild.ParentId))]
    public IReadOnlyList<UnsignedKeyChild> Children { get; set; } = new List<UnsignedKeyChild>();
}

/// <summary>Child referencing the parent's <c>uint</c> key via a <c>uint</c> foreign key.</summary>
[InquiryTable("UnsignedKeyChild")]
public sealed class UnsignedKeyChild
{
    [InquiryKey("Id")]
    public uint Id { get; set; }

    [InquiryColumn("ParentId")]
    public uint ParentId { get; set; }

    [InquiryColumn("Label")]
    public string Label { get; set; } = string.Empty;
}

public partial class UnsignedKeyParentStore : InquiryStore<UnsignedKeyParent>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(UnsignedKeyParent parent, CancellationToken cancellationToken = default);

    [InquirySelectOneByKeyEager]
    public partial Task<UnsignedKeyParent?> GetWithChildrenAsync(uint id, CancellationToken cancellationToken = default);
}

public partial class UnsignedKeyChildStore : InquiryStore<UnsignedKeyChild>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(UnsignedKeyChild child, CancellationToken cancellationToken = default);
}
