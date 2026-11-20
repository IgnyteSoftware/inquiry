using Inquiry.Stores;

namespace Inquiry.Journeys;

public partial class CustomerStore : InquiryStore<Customer>
{
    [InquiryInsert]
    public partial Task<Customer?> InsertReturningAsync(Customer customer, CancellationToken ct = default);

    [InquirySelectOneByKey]
    public partial Task<Customer?> SelectByKeyAsync(long? id, CancellationToken ct = default);
}

public partial class SalesOrderStore : InquiryStore<SalesOrder>
{
    [InquiryInsert]
    public partial Task<SalesOrder?> InsertReturningAsync(SalesOrder order, CancellationToken ct = default);

    // Loads every relation: Customer and Lines.
    [InquirySelectOneByKeyEager]
    public partial Task<SalesOrder?> SelectWithRelationsAsync(long? id, CancellationToken ct = default);

    [InquirySelectOneByKeyEager]
    [InquiryInclude(nameof(SalesOrder.Lines))]
    public partial Task<SalesOrder?> SelectWithLinesAsync(long? id, CancellationToken ct = default);
}

public partial class OrderLineStore : InquiryStore<OrderLine>
{
    [InquiryInsert]
    public partial Task<int> InsertAllAsync(IReadOnlyList<OrderLine> lines, CancellationToken ct = default);
}

public partial class UserStore : InquiryStore<User>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(User user, CancellationToken ct = default);

    [InquirySelectOneByKeyEager]
    public partial Task<User?> SelectWithRolesAsync(int id, CancellationToken ct = default);
}

public partial class RoleStore : InquiryStore<Role>
{
    [InquiryInsert]
    public partial Task<int> InsertAllAsync(IReadOnlyList<Role> roles, CancellationToken ct = default);
}

public partial class UserRoleStore : InquiryStore<UserRole>
{
    [InquiryInsert]
    public partial Task<int> LinkAsync(UserRole link, CancellationToken ct = default);

    [InquiryDelete]
    public partial Task<bool> UnlinkAsync(int userId, int roleId, CancellationToken ct = default);
}

public partial class ArticleStore : InquiryStore<Article>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(Article article, CancellationToken ct = default);

    [InquirySelectOneByKey]
    public partial Task<Article?> SelectByKeyAsync(int id, CancellationToken ct = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(Article article, CancellationToken ct = default);
}

public partial class DocumentStore : InquiryStore<Document>
{
    [InquiryInsert]
    public partial Task<int> InsertAllAsync(IReadOnlyList<Document> documents, CancellationToken ct = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<Document>> SelectAllAsync(CancellationToken ct = default);

    [InquirySelectOneByKey]
    public partial Task<Document?> SelectByKeyAsync(int id, CancellationToken ct = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(Document document, CancellationToken ct = default);

    [InquiryDelete]
    public partial Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}

public partial class SupplierStore : InquiryStore<Supplier>
{
    [InquiryInsert]
    public partial Task<Supplier?> InsertReturningAsync(Supplier supplier, CancellationToken ct = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<Supplier>> SelectAllAsync(CancellationToken ct = default);

    [InquirySelectOneByKey]
    public partial Task<Supplier?> SelectByKeyAsync(int? id, CancellationToken ct = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(Supplier supplier, CancellationToken ct = default);

    [InquiryDelete]
    public partial Task<bool> DeleteAsync(int? id, CancellationToken ct = default);
}
