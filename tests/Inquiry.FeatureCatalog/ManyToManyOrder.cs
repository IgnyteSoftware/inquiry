using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

public static class M2MMaterializationProbe
{
    public const string Sentinel = "__UNRELATED_SENTINEL__";
    private static readonly object Gate = new();
    private static HashSet<string> _excludedTitles = new();
    private static HashSet<long> _excludedJunctionProductIds = new();
    private static int _childReads;
    private static int _excludedChildReads;
    private static int _junctionReads;
    private static int _excludedJunctionReads;

    public static int ChildReads => Volatile.Read(ref _childReads);
    public static int ExcludedChildReads => Volatile.Read(ref _excludedChildReads);
    public static int JunctionReads => Volatile.Read(ref _junctionReads);
    public static int ExcludedJunctionReads => Volatile.Read(ref _excludedJunctionReads);

    public static void Reset(IEnumerable<string> excludedTitles, IEnumerable<long> excludedJunctionProductIds)
    {
        lock (Gate)
        {
            _excludedTitles = new HashSet<string>(excludedTitles);
            _excludedJunctionProductIds = new HashSet<long>(excludedJunctionProductIds);
        }
        Volatile.Write(ref _childReads, 0);
        Volatile.Write(ref _excludedChildReads, 0);
        Volatile.Write(ref _junctionReads, 0);
        Volatile.Write(ref _excludedJunctionReads, 0);
    }

    internal static void RecordChild(string value)
    {
        Interlocked.Increment(ref _childReads);
        lock (Gate)
        {
            if (_excludedTitles.Contains(value)) Interlocked.Increment(ref _excludedChildReads);
        }
    }

    internal static void RecordJunction(long productId)
    {
        Interlocked.Increment(ref _junctionReads);
        lock (Gate)
        {
            if (_excludedJunctionProductIds.Contains(productId)) Interlocked.Increment(ref _excludedJunctionReads);
        }
    }
}

public sealed class M2MTitleConverter : IInquiryValueConverter<string, string>
{
    public string ToProvider(string model) => model;
    public string FromProvider(string provider)
    {
        M2MMaterializationProbe.RecordChild(provider);
        return provider;
    }
}

public sealed class M2MJunctionProductIdConverter : IInquiryValueConverter<long, long>
{
    public long ToProvider(long model) => model;
    public long FromProvider(long provider)
    {
        M2MMaterializationProbe.RecordJunction(provider);
        return provider;
    }
}

public sealed class M2MExcludedRowsScenarioResult
{
    public long DeletedParentId { get; init; }
    public string DeletedParentIncludedTitle { get; init; } = string.Empty;
    public IReadOnlyList<string> DefaultExcludedTitles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<long> DefaultExcludedProductIds { get; init; } = Array.Empty<long>();
    public IReadOnlyList<string> IncludeDeletedExcludedTitles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<long> IncludeDeletedExcludedProductIds { get; init; } = Array.Empty<long>();
}

public static class M2MExcludedRowsScenario
{
    public static readonly string[] ExcludedTitles =
    {
        "Deleted child",
        "Inactive child",
        "Deleted junction",
        "Inactive junction",
    };

    public static async Task<M2MExcludedRowsScenarioResult> SeedAsync(
        M2MOrderStore orders,
        M2MProductStore products,
        M2MOrderProductStore links,
        long participatingOrderId)
    {
        var defaultExcludedTitles = new List<string>();
        var defaultExcludedIds = new List<long>();
        var includeExcludedTitles = new List<string>();
        var includeExcludedIds = new List<long>();

        var participatingChild = (await products.InsertAsync(new M2MProduct
        {
            Title = "Participating child",
        }))!.Id;
        await links.LinkAsync(new M2MOrderProduct
        {
            OrderId = participatingOrderId,
            ProductId = participatingChild,
        });

        for (var i = 0; i < 63; i++)
        {
            await products.InsertAsync(new M2MProduct { Title = "Unrelated " + i });
        }

        await products.InsertAsync(new M2MProduct { Title = M2MMaterializationProbe.Sentinel });
        defaultExcludedTitles.Add(M2MMaterializationProbe.Sentinel);
        includeExcludedTitles.Add(M2MMaterializationProbe.Sentinel);

        var deletedChild = (await products.InsertAsync(new M2MProduct
        {
            Title = ExcludedTitles[0],
            IsDeleted = true,
        }))!.Id;
        await links.LinkAsync(new M2MOrderProduct { OrderId = participatingOrderId, ProductId = deletedChild });
        defaultExcludedTitles.Add(ExcludedTitles[0]);
        defaultExcludedIds.Add(deletedChild);
        includeExcludedTitles.Add(ExcludedTitles[0]);
        includeExcludedIds.Add(deletedChild);

        var inactiveChild = (await products.InsertAsync(new M2MProduct
        {
            Title = ExcludedTitles[1],
            IsActive = false,
        }))!.Id;
        await links.LinkAsync(new M2MOrderProduct { OrderId = participatingOrderId, ProductId = inactiveChild });
        defaultExcludedTitles.Add(ExcludedTitles[1]);
        defaultExcludedIds.Add(inactiveChild);
        includeExcludedTitles.Add(ExcludedTitles[1]);
        includeExcludedIds.Add(inactiveChild);

        var deletedJunction = (await products.InsertAsync(new M2MProduct { Title = ExcludedTitles[2] }))!.Id;
        await links.LinkAsync(new M2MOrderProduct
        {
            OrderId = participatingOrderId,
            ProductId = deletedJunction,
            IsDeleted = true,
        });
        defaultExcludedTitles.Add(ExcludedTitles[2]);
        defaultExcludedIds.Add(deletedJunction);
        includeExcludedTitles.Add(ExcludedTitles[2]);
        includeExcludedIds.Add(deletedJunction);

        var inactiveJunction = (await products.InsertAsync(new M2MProduct { Title = ExcludedTitles[3] }))!.Id;
        await links.LinkAsync(new M2MOrderProduct
        {
            OrderId = participatingOrderId,
            ProductId = inactiveJunction,
            IsActive = false,
        });
        defaultExcludedTitles.Add(ExcludedTitles[3]);
        defaultExcludedIds.Add(inactiveJunction);
        includeExcludedTitles.Add(ExcludedTitles[3]);
        includeExcludedIds.Add(inactiveJunction);

        var deletedParent = (await orders.InsertAsync(new M2MOrder
        {
            Name = "Deleted parent",
            IsDeleted = true,
        }))!;
        var inactiveParent = (await orders.InsertAsync(new M2MOrder
        {
            Name = "Inactive parent",
            IsActive = false,
        }))!;
        var deletedParentIncludedTitle = "Deleted parent child 0";
        for (var i = 0; i < 16; i++)
        {
            var deletedParentTitle = "Deleted parent child " + i;
            var deletedParentChild = (await products.InsertAsync(new M2MProduct { Title = deletedParentTitle }))!.Id;
            await links.LinkAsync(new M2MOrderProduct { OrderId = deletedParent.Id, ProductId = deletedParentChild });
            defaultExcludedTitles.Add(deletedParentTitle);
            defaultExcludedIds.Add(deletedParentChild);

            var inactiveParentTitle = "Inactive parent child " + i;
            var inactiveParentChild = (await products.InsertAsync(new M2MProduct { Title = inactiveParentTitle }))!.Id;
            await links.LinkAsync(new M2MOrderProduct { OrderId = inactiveParent.Id, ProductId = inactiveParentChild });
            defaultExcludedTitles.Add(inactiveParentTitle);
            defaultExcludedIds.Add(inactiveParentChild);
            includeExcludedTitles.Add(inactiveParentTitle);
            includeExcludedIds.Add(inactiveParentChild);
        }

        return new M2MExcludedRowsScenarioResult
        {
            DeletedParentId = deletedParent.Id,
            DeletedParentIncludedTitle = deletedParentIncludedTitle,
            DefaultExcludedTitles = defaultExcludedTitles,
            DefaultExcludedProductIds = defaultExcludedIds,
            IncludeDeletedExcludedTitles = includeExcludedTitles,
            IncludeDeletedExcludedProductIds = includeExcludedIds,
        };
    }
}

[InquiryTable("M2MOrder")]
public sealed class M2MOrder
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn("IsDeleted"), InquirySoftDelete]
    public bool IsDeleted { get; set; }

    [InquiryColumn("IsActive"), InquiryGlobalFilter]
    public bool IsActive { get; set; } = true;

    [InquiryManyToMany(typeof(M2MOrderProduct), nameof(M2MOrderProduct.OrderId), nameof(M2MOrderProduct.ProductId))]
    public List<M2MProduct> Products { get; set; } = new();
}

[InquiryTable("M2MProduct")]
public sealed class M2MProduct
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Title", Converter = typeof(M2MTitleConverter))]
    public string Title { get; set; } = string.Empty;

    [InquiryColumn("IsDeleted"), InquirySoftDelete]
    public bool IsDeleted { get; set; }

    [InquiryColumn("IsActive"), InquiryGlobalFilter]
    public bool IsActive { get; set; } = true;
}

[InquiryTable("M2MOrderProduct")]
public sealed class M2MOrderProduct
{
    [InquiryKey]
    public long OrderId { get; set; }

    [InquiryKey(Converter = typeof(M2MJunctionProductIdConverter))]
    public long ProductId { get; set; }

    [InquiryColumn("IsDeleted"), InquirySoftDelete]
    public bool IsDeleted { get; set; }

    [InquiryColumn("IsActive"), InquiryGlobalFilter]
    public bool IsActive { get; set; } = true;
}

public partial class M2MOrderStore : InquiryStore<M2MOrder>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<M2MOrder?> InsertAsync(M2MOrder order, CancellationToken cancellationToken = default);

    [InquirySelectOneByKeyEager]
    public partial Task<M2MOrder?> GetWithProductsAsync(long id, CancellationToken cancellationToken = default);

    [InquirySelectAllEager]
    public partial IAsyncEnumerable<M2MOrder> AllWithProductsAsync(CancellationToken cancellationToken = default);

    [InquirySelectAllEager(IncludeDeleted = true)]
    public partial IAsyncEnumerable<M2MOrder> AllIncludingDeletedWithProductsAsync(CancellationToken cancellationToken = default);
}

public partial class M2MProductStore : InquiryStore<M2MProduct>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<M2MProduct?> InsertAsync(M2MProduct product, CancellationToken cancellationToken = default);
}

public partial class M2MOrderProductStore : InquiryStore<M2MOrderProduct>
{
    [InquiryInsert]
    public partial Task<int> LinkAsync(M2MOrderProduct link, CancellationToken cancellationToken = default);
}

// ---------------------------------------------------------------------------------------------
// Composite-key related entity (#80 Phase A). M2MTag is keyed (TenantId, Slug) — client-supplied,
// since INQ011 forbids generated columns in a composite key — and the junction names one foreign-key
// property per key column, in the tag's key-declaration order. The pair (TenantId, Slug) is chosen so
// each component occurs under more than one value of the other: a join that matched on only one
// component would return rows the association does not link.
// ---------------------------------------------------------------------------------------------

[InquiryTable("M2MPost")]
public sealed class M2MPost
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Title")]
    public string Title { get; set; } = string.Empty;

    [InquiryColumn("IsDeleted"), InquirySoftDelete]
    public bool IsDeleted { get; set; }

    [InquiryManyToMany(typeof(M2MPostTag), nameof(M2MPostTag.PostId), nameof(M2MPostTag.TenantId), nameof(M2MPostTag.Slug))]
    public List<M2MTag> Tags { get; set; } = new();
}

[InquiryTable("M2MTag")]
public sealed class M2MTag
{
    [InquiryKey]
    public int TenantId { get; set; }

    [InquiryKey(Length = 64)]
    public string Slug { get; set; } = string.Empty;

    [InquiryColumn("Label")]
    public string Label { get; set; } = string.Empty;

    [InquiryColumn("IsDeleted"), InquirySoftDelete]
    public bool IsDeleted { get; set; }
}

[InquiryTable("M2MPostTag")]
public sealed class M2MPostTag
{
    [InquiryKey]
    public long PostId { get; set; }

    [InquiryKey]
    public int TenantId { get; set; }

    [InquiryKey(Length = 64)]
    public string Slug { get; set; } = string.Empty;

    [InquiryColumn("IsDeleted"), InquirySoftDelete]
    public bool IsDeleted { get; set; }
}

public partial class M2MPostStore : InquiryStore<M2MPost>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<M2MPost?> InsertAsync(M2MPost post, CancellationToken cancellationToken = default);

    [InquirySelectOneByKeyEager]
    public partial Task<M2MPost?> GetWithTagsAsync(long id, CancellationToken cancellationToken = default);

    [InquirySelectAllEager]
    public partial IAsyncEnumerable<M2MPost> AllWithTagsAsync(CancellationToken cancellationToken = default);

    [InquirySelectAllEager(IncludeDeleted = true)]
    public partial IAsyncEnumerable<M2MPost> AllIncludingDeletedWithTagsAsync(CancellationToken cancellationToken = default);
}

public partial class M2MTagStore : InquiryStore<M2MTag>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(M2MTag tag, CancellationToken cancellationToken = default);
}

public partial class M2MPostTagStore : InquiryStore<M2MPostTag>
{
    [InquiryInsert]
    public partial Task<int> LinkAsync(M2MPostTag link, CancellationToken cancellationToken = default);
}

// ---------------------------------------------------------------------------------------------
// Auto-managed junction (#80 Phase B). Neither side names a junction: Inquiry synthesizes
// M2MAuthor_M2MBook with columns M2MAuthor_Id / M2MBook_Id, derived from the two table names sorted
// ordinally. Declared from BOTH sides so the canonical naming is exercised end to end — a
// parent-first derivation would produce two tables here.
//
// Keys are client-supplied so the fixture can seed authors, books, AND links from DDL: an
// auto-managed junction is read-only, so there is no store that could insert a link row.
// ---------------------------------------------------------------------------------------------

[InquiryTable("M2MAuthor")]
public sealed class M2MAuthor
{
    [InquiryKey]
    public long Id { get; set; }

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn("IsDeleted"), InquirySoftDelete]
    public bool IsDeleted { get; set; }

    [InquiryManyToMany]
    public List<M2MBook> Books { get; set; } = new();
}

[InquiryTable("M2MBook")]
public sealed class M2MBook
{
    [InquiryKey]
    public long Id { get; set; }

    [InquiryColumn("Title")]
    public string Title { get; set; } = string.Empty;

    [InquiryColumn("IsDeleted"), InquirySoftDelete]
    public bool IsDeleted { get; set; }

    [InquiryManyToMany]
    public List<M2MAuthor> Authors { get; set; } = new();
}

public partial class M2MAuthorStore : InquiryStore<M2MAuthor>
{
    [InquirySelectOneByKeyEager]
    public partial Task<M2MAuthor?> GetWithBooksAsync(long id, CancellationToken cancellationToken = default);

    [InquirySelectAllEager]
    public partial IAsyncEnumerable<M2MAuthor> AllWithBooksAsync(CancellationToken cancellationToken = default);

    [InquirySelectAllEager(IncludeDeleted = true)]
    public partial IAsyncEnumerable<M2MAuthor> AllIncludingDeletedWithBooksAsync(CancellationToken cancellationToken = default);
}

public partial class M2MBookStore : InquiryStore<M2MBook>
{
    [InquirySelectAllEager]
    public partial IAsyncEnumerable<M2MBook> AllWithAuthorsAsync(CancellationToken cancellationToken = default);
}
