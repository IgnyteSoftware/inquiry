using Inquiry;
using Inquiry.DependencyInjection;
using Inquiry.Entities;
using Inquiry.Stores;
using Microsoft.Extensions.DependencyInjection;

[assembly: InquiryDialect("SqlServer")]

namespace Catalog.V1;

[InquiryTable("CatalogItems")]
public sealed class CatalogItem
{
    [InquiryKey(IsGenerated = true)]
    public int Id { get; set; }

    [InquiryColumn(Length = 32)]
    public string Code { get; set; } = "";
}

public partial class CatalogItemStore : InquiryStore<CatalogItem>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(CatalogItem item, CancellationToken ct = default);

    // Compare.In binds a SQL Server table-valued parameter whose type matches Code's facets.
    [InquirySelectAllByPredicate]
    [InquiryWhere(nameof(CatalogItem.Code), Compare.In)]
    public partial Task<IReadOnlyList<CatalogItem>> ByCodesAsync(
        IReadOnlyList<string> codes, CancellationToken ct = default);
}

// InquiryGeneratedSchema is internal to this assembly, so expose what deployment needs.
public static class CatalogSchema
{
    public const string Ddl = Inquiry.Generated.InquiryGeneratedSchema.Ddl;
    public const string ProviderArtifactsDdl = Inquiry.Generated.InquiryGeneratedSchema.ProviderArtifactsDdl;
    public const string ProviderArtifactsValidationSql = Inquiry.Generated.InquiryGeneratedSchema.ProviderArtifactsValidationSql;
}

public static class CatalogRegistration
{
    public static IServiceCollection AddCatalogV1(this IServiceCollection services)
        => services.AddInquiryGeneratedStores();
}
