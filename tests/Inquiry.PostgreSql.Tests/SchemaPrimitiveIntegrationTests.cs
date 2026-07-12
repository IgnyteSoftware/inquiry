using Inquiry.Entities;
using Inquiry.FeatureCatalog;
using Inquiry.Generated;
using Inquiry.PostgreSql.Tests.Fixtures;
using Npgsql;
using Xunit;

namespace Inquiry.PostgreSql.Tests;

[InquiryTable("PrimitiveCovering")]
[InquiryIndex(nameof(Category), Name = "IX_PrimitiveCovering_Category", Include = new[] { nameof(Payload) })]
public sealed class PrimitiveCovering
{
    [InquiryKey] public long Id { get; set; }
    [InquiryColumn] public int Category { get; set; }
    [InquiryColumn(Length = 64)] public string Payload { get; set; } = string.Empty;
}

[Collection(PostgreSqlCollection.Name)]
public sealed class SchemaPrimitiveIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    public SchemaPrimitiveIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task GeneratedSchemaPrimitivesAreCatalogedAndEnforced()
    {
        var ddl = GeneratedSchemaDdl.Extract(InquiryGeneratedSchema.Ddl, "PrimitiveParent", "PrimitiveOptionalParent", "PrimitiveChild", "PrimitiveCovering");
        Assert.Contains("(\"Category\") INCLUDE (\"Payload\")", ddl);
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, ddl, "genprimitive");
        await using var c = new NpgsqlConnection(harness.ConnectionString); await c.OpenAsync();
        Assert.Equal("ParentId,code", await StringAsync(c, "SELECT string_agg(a.attname, ',' ORDER BY k.ordinality) FROM pg_index i CROSS JOIN LATERAL unnest(i.indkey) WITH ORDINALITY k(attnum, ordinality) JOIN pg_attribute a ON a.attrelid=i.indrelid AND a.attnum=k.attnum WHERE i.indexrelid='\"IX_PrimitiveChild_Parent_Code\"'::regclass AND k.ordinality<=i.indnkeyatts"));
        Assert.Equal("TenantId,code", await StringAsync(c, "SELECT string_agg(a.attname, ',' ORDER BY k.ordinality) FROM pg_index i CROSS JOIN LATERAL unnest(i.indkey) WITH ORDINALITY k(attnum, ordinality) JOIN pg_attribute a ON a.attrelid=i.indrelid AND a.attnum=k.attnum WHERE i.indexrelid='\"UX_PrimitiveChild_Tenant_Code\"'::regclass AND k.ordinality<=i.indnkeyatts AND i.indisunique"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM pg_indexes WHERE indexname='IX_PrimitiveCovering_Category' AND indexdef LIKE '%INCLUDE (\"Payload\")%'"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM pg_constraint WHERE conname='CK_PrimitiveChild_Quantity' AND contype='c'"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM pg_constraint WHERE conname='CK_PrimitiveChild_Code' AND contype='c'"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM pg_constraint WHERE conname='FK_PrimitiveChild_Parent' AND confdeltype='c'"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM pg_constraint WHERE conname='FK_PrimitiveChild_OptionalParent' AND confdeltype='n'"));
        await ExecAsync(c, "INSERT INTO \"PrimitiveParent\" (\"Id\") VALUES (1); INSERT INTO \"PrimitiveOptionalParent\" (\"Id\") VALUES (1); INSERT INTO \"PrimitiveChild\" (\"Id\",\"ParentId\",\"OptionalParentId\",\"TenantId\",\"code\",\"quantity\") VALUES (1,1,1,7,'A',1)");
        await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(c, "INSERT INTO \"PrimitiveChild\" (\"Id\",\"ParentId\",\"TenantId\",\"code\",\"quantity\") VALUES (2,1,7,'A',1)"));
        await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(c, "INSERT INTO \"PrimitiveChild\" (\"Id\",\"ParentId\",\"TenantId\",\"code\",\"quantity\") VALUES (3,1,8,'B',-1)"));
        await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(c, "INSERT INTO \"PrimitiveChild\" (\"Id\",\"ParentId\",\"TenantId\",\"code\",\"quantity\") VALUES (4,1,9,'',1)"));
        await ExecAsync(c, "DELETE FROM \"PrimitiveOptionalParent\" WHERE \"Id\"=1");
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM \"PrimitiveChild\" WHERE \"OptionalParentId\" IS NULL"));
        await ExecAsync(c, "DELETE FROM \"PrimitiveParent\" WHERE \"Id\"=1");
        Assert.Equal(0, await ScalarAsync(c, "SELECT COUNT(*) FROM \"PrimitiveChild\""));
        await ExecAsync(c, GeneratedSchemaDdl.Extract(InquiryGeneratedSchema.Ddl, "UpdateCascadeParent", "UpdateCascadeChild"));
        await ExecAsync(c, "INSERT INTO \"UpdateCascadeParent\" VALUES(10); INSERT INTO \"UpdateCascadeChild\" VALUES(1,10); UPDATE \"UpdateCascadeParent\" SET \"Id\"=11 WHERE \"Id\"=10");
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM \"UpdateCascadeChild\" WHERE \"ParentId\"=11"));
    }
    private static async Task ExecAsync(NpgsqlConnection c, string sql) { await using var cmd = c.CreateCommand(); cmd.CommandText = sql; await cmd.ExecuteNonQueryAsync(); }
    private static async Task<int> ScalarAsync(NpgsqlConnection c, string sql) { await using var cmd = c.CreateCommand(); cmd.CommandText = sql; return Convert.ToInt32(await cmd.ExecuteScalarAsync()); }
    private static async Task<string?> StringAsync(NpgsqlConnection c, string sql) { await using var cmd = c.CreateCommand(); cmd.CommandText = sql; return Convert.ToString(await cmd.ExecuteScalarAsync()); }
}
