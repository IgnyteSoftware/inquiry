using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

// ---------------------------------------------------------------------------
// #70 live fixture for the 1-parent + 2-relations grid shape. EagerMixedPost
// carries BOTH a to-one reference (Author, via its own AuthorId) and a to-many
// collection (Tags, via EagerMixedTag.PostId), so the eager emitter batches
// three SELECTs into one command: the collection filters by the parent key,
// the reference resolves server-side through the _ByKey scalar subquery.
//
// The generator-level twin is MixedRelationEagerSource in
// tests/Inquiry.Generators.Tests/InquiryGeneratorTests.EagerGrid.cs. This is
// the live half — no provider previously exercised a two-relation eager load.
//
// Keys are client-supplied (no IDENTITY/SEQUENCE) so the tests pin ids and the
// DDL carries no dialect variance. No FK constraints, matching the convention
// of the other eager repro tables in FeatureSchema.
// ---------------------------------------------------------------------------

/// <summary>Target of the to-one reference relation.</summary>
[InquiryTable("EagerMixedAuthor")]
public sealed class EagerMixedAuthor
{
    [InquiryKey("Id")]
    public int Id { get; set; }

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>Child of the to-many collection relation.</summary>
[InquiryTable("EagerMixedTag")]
public sealed class EagerMixedTag
{
    [InquiryKey("Id")]
    public int Id { get; set; }

    [InquiryColumn("PostId")]
    public int PostId { get; set; }

    [InquiryColumn("Label")]
    public string Label { get; set; } = string.Empty;
}

/// <summary>Parent carrying one reference relation and one collection relation.</summary>
[InquiryTable("EagerMixedPost")]
public sealed class EagerMixedPost
{
    [InquiryKey("Id")]
    public int Id { get; set; }

    [InquiryColumn("AuthorId")]
    public int AuthorId { get; set; }

    [InquiryColumn("Title")]
    public string Title { get; set; } = string.Empty;

    // Reference: the FK lives on THIS entity, so the ctor arg names the local property.
    [InquiryRelation(nameof(AuthorId))]
    public EagerMixedAuthor? Author { get; set; }

    // Collection: the FK lives on the CHILD, so the ctor arg names the child's property.
    [InquiryRelation(nameof(EagerMixedTag.PostId))]
    public IReadOnlyList<EagerMixedTag> Tags { get; set; } = new List<EagerMixedTag>();
}

public partial class EagerMixedAuthorStore : InquiryStore<EagerMixedAuthor>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(EagerMixedAuthor author, CancellationToken cancellationToken = default);
}

public partial class EagerMixedTagStore : InquiryStore<EagerMixedTag>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(EagerMixedTag tag, CancellationToken cancellationToken = default);
}

public partial class EagerMixedPostStore : InquiryStore<EagerMixedPost>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(EagerMixedPost post, CancellationToken cancellationToken = default);

    [InquirySelectOneByKeyEager]
    public partial Task<EagerMixedPost?> GetWithAuthorAndTagsAsync(int id, CancellationToken cancellationToken = default);

    [InquirySelectAllEager]
    public partial IAsyncEnumerable<EagerMixedPost> SelectAllWithAuthorAndTagsAsync(CancellationToken cancellationToken = default);
}
