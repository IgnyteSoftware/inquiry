using Inquiry.FeatureCatalog;
using Inquiry.Generated;
using Inquiry.Oracle.Tests.Fixtures;
using Oracle.ManagedDataAccess.Client;
using Xunit;

namespace Inquiry.Oracle.Tests;

[Collection(OracleCollection.Name)]
public sealed class SchemaPrimitiveIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public SchemaPrimitiveIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task GeneratedSchemaPrimitivesAreCatalogedAndEnforced()
    {
        var ddl = GeneratedSchemaDdl.Extract(InquiryGeneratedSchema.Ddl, "PrimitiveParent", "PrimitiveOptionalParent", "PrimitiveChild");
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, ddl, "genprimitive");
        await using var c = new OracleConnection(harness.ConnectionString); await c.OpenAsync();
        Assert.Equal("PARENTID,CODE", await StringAsync(c, "SELECT LISTAGG(COLUMN_NAME, ',') WITHIN GROUP (ORDER BY COLUMN_POSITION) FROM USER_IND_COLUMNS WHERE INDEX_NAME='IX_PRIMITIVECHILD_PARENT_CODE'"));
        Assert.Equal("TENANTID,CODE", await StringAsync(c, "SELECT LISTAGG(COLUMN_NAME, ',') WITHIN GROUP (ORDER BY COLUMN_POSITION) FROM USER_IND_COLUMNS WHERE INDEX_NAME='UX_PRIMITIVECHILD_TENANT_CODE'"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM USER_INDEXES WHERE INDEX_NAME='UX_PRIMITIVECHILD_TENANT_CODE' AND UNIQUENESS='UNIQUE'"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM USER_CONSTRAINTS WHERE CONSTRAINT_NAME='CK_PRIMITIVECHILD_QUANTITY' AND CONSTRAINT_TYPE='C'"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM USER_CONSTRAINTS WHERE CONSTRAINT_NAME='CK_PRIMITIVECHILD_CODE' AND CONSTRAINT_TYPE='C'"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM USER_CONSTRAINTS WHERE CONSTRAINT_NAME='FK_PRIMITIVECHILD_PARENT' AND DELETE_RULE='CASCADE'"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM USER_CONSTRAINTS WHERE CONSTRAINT_NAME='FK_PRIMITIVECHILD_OPTIONALPARENT' AND DELETE_RULE='SET NULL'"));
        await ExecAsync(c, "INSERT INTO PrimitiveParent VALUES(1)"); await ExecAsync(c, "INSERT INTO PrimitiveOptionalParent VALUES(1)"); await ExecAsync(c, "INSERT INTO PrimitiveChild VALUES(1,1,1,7,'A',1)");
        await Assert.ThrowsAsync<OracleException>(() => ExecAsync(c, "INSERT INTO PrimitiveChild VALUES(2,1,NULL,7,'A',1)"));
        await Assert.ThrowsAsync<OracleException>(() => ExecAsync(c, "INSERT INTO PrimitiveChild VALUES(3,1,NULL,8,'B',-1)"));
        await Assert.ThrowsAsync<OracleException>(() => ExecAsync(c, "INSERT INTO PrimitiveChild VALUES(4,1,NULL,9,'',1)"));
        await ExecAsync(c, "DELETE FROM PrimitiveOptionalParent WHERE Id=1"); Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM PrimitiveChild WHERE OptionalParentId IS NULL"));
        await ExecAsync(c, "DELETE FROM PrimitiveParent WHERE Id=1"); Assert.Equal(0, await ScalarAsync(c, "SELECT COUNT(*) FROM PrimitiveChild"));
    }
    private static async Task ExecAsync(OracleConnection c, string sql) { await using var cmd = c.CreateCommand(); cmd.CommandText = sql; await cmd.ExecuteNonQueryAsync(); }
    private static async Task<int> ScalarAsync(OracleConnection c, string sql) { await using var cmd = c.CreateCommand(); cmd.CommandText = sql; return Convert.ToInt32(await cmd.ExecuteScalarAsync()); }
    private static async Task<string?> StringAsync(OracleConnection c, string sql) { await using var cmd = c.CreateCommand(); cmd.CommandText = sql; return Convert.ToString(await cmd.ExecuteScalarAsync()); }
}
