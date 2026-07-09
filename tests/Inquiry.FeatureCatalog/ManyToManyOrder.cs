using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

[InquiryTable("M2MOrder")]
public sealed class M2MOrder
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;

    [InquiryManyToMany(typeof(M2MOrderProduct), nameof(M2MOrderProduct.OrderId), nameof(M2MOrderProduct.ProductId))]
    public List<M2MProduct> Products { get; set; } = new();
}

[InquiryTable("M2MProduct")]
public sealed class M2MProduct
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Title")]
    public string Title { get; set; } = string.Empty;
}

[InquiryTable("M2MOrderProduct")]
public sealed class M2MOrderProduct
{
    [InquiryKey]
    public long OrderId { get; set; }

    [InquiryKey]
    public long ProductId { get; set; }
}

public partial class M2MOrderStore : InquiryStore<M2MOrder>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<M2MOrder?> InsertAsync(M2MOrder order, CancellationToken cancellationToken = default);

    [InquirySelectOneByKeyEager]
    public partial Task<M2MOrder?> GetWithProductsAsync(long id, CancellationToken cancellationToken = default);

    [InquirySelectAllEager]
    public partial IAsyncEnumerable<M2MOrder> AllWithProductsAsync(CancellationToken cancellationToken = default);
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
