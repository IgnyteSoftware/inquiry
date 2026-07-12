using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Generated;
using Inquiry.IntegrationTesting;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Stores;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Inquiry.SqlServer.Tests;

[InquiryTable("GeneratedRowversionDocument")]
public sealed class GeneratedRowversionDocument
{
    [InquiryKey]
    public long Id { get; set; }

    [InquiryColumn]
    public string Name { get; set; } = string.Empty;

    [InquiryConcurrencyToken(DatabaseGenerated = true)]
    public byte[] Version { get; set; } = [];
}

public partial class GeneratedRowversionDocumentStore : InquiryStore<GeneratedRowversionDocument>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<GeneratedRowversionDocument?> InsertAsync(GeneratedRowversionDocument document, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<GeneratedRowversionDocument?> SelectAsync(long id, CancellationToken cancellationToken = default);

    [InquiryUpdate(ReturnEntity = true)]
    public partial Task<GeneratedRowversionDocument?> UpdateAsync(GeneratedRowversionDocument document, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public partial Task<bool> DeleteAsync(GeneratedRowversionDocument document, CancellationToken cancellationToken = default);

    [InquiryBulkInsert]
    public partial Task<long> BulkInsertAsync(IEnumerable<GeneratedRowversionDocument> documents, CancellationToken cancellationToken = default);
}

/// <summary>Verifies Inquiry's own generated DDL (InquiryGeneratedSchema.Ddl) stands up a working
/// schema on real SQL Server: it executes, supports CRUD, and is structurally correct
/// (tables/PKs/FKs). The strict full-contract index/column check runs against the hand-written DDL.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class GeneratedDdlIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public GeneratedDdlIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InquiryGeneratedSchemaStandsUpAndRoundTripsCrud()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, InquiryGeneratedSchema.Ddl, "gends", provisionProviderArtifacts: false);

        var categories = harness.GetRequiredService<CategoryStore>();
        var inserted = await categories.InsertReturningAsync(new Category { CategoryName = "Beverages" });
        Assert.NotNull(inserted);
        Assert.True(inserted!.CategoryID > 0);
        Assert.NotNull(await categories.SelectByKeyAsync(inserted.CategoryID));

        await using var conn = new SqlConnection(harness.ConnectionString);
        await conn.OpenAsync();
        await using (var validation = conn.CreateCommand())
        {
            validation.CommandText = InquiryGeneratedSchema.ProviderArtifactsValidationSql;
            await using var reader = await validation.ExecuteReaderAsync();
            Assert.False(await reader.ReadAsync());
        }
        var actual = await new SqlServerSchemaIntrospector().ReadAsync(conn);
        SchemaFidelity.AssertStructure(ExpectedNorthwindSchema.Schema, actual);
    }

    [SkippableFact]
    public async Task GeneratedRowversionSupportsCrudStaleConflictsAndBulkOmission()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, InquiryGeneratedSchema.Ddl, "genrv", provisionProviderArtifacts: false);
        var store = harness.GetRequiredService<GeneratedRowversionDocumentStore>();

        var inserted = await store.InsertAsync(new GeneratedRowversionDocument { Id = 1, Name = "v1" });
        Assert.NotNull(inserted);
        Assert.Equal(8, inserted!.Version.Length);
        var stale = new GeneratedRowversionDocument { Id = inserted.Id, Name = "stale", Version = inserted.Version.ToArray() };

        inserted.Name = "v2";
        var updated = await store.UpdateAsync(inserted);
        Assert.NotNull(updated);
        Assert.Equal(8, updated!.Version.Length);
        Assert.False(inserted.Version.SequenceEqual(updated.Version));
        Assert.Null(await store.UpdateAsync(stale));
        Assert.False(await store.DeleteAsync(stale));
        Assert.Equal("v2", (await store.SelectAsync(1))!.Name);

        var written = await store.BulkInsertAsync(new[]
        {
            new GeneratedRowversionDocument { Id = 2, Name = "bulk-1" },
            new GeneratedRowversionDocument { Id = 3, Name = "bulk-2" },
        });
        Assert.Equal(2, written);
        Assert.Equal(8, (await store.SelectAsync(2))!.Version.Length);
        Assert.Equal(8, (await store.SelectAsync(3))!.Version.Length);
    }
}
