using Inquiry.FeatureCatalog;
using Inquiry.Generated;
using Inquiry.MySql.Tests.Fixtures;
using MySqlConnector;
using Xunit;

namespace Inquiry.MySql.Tests;

[Collection(MySqlCollection.Name)]
public sealed class SchemaPrimitiveIntegrationTests
{
    private readonly MySqlContainerFixture _f;

    public SchemaPrimitiveIntegrationTests(MySqlContainerFixture f) => _f = f;

    [SkippableFact]
    public async Task GeneratedSchemaPrimitivesAreCatalogedAndEnforced()
    {
        var d = GeneratedSchemaDdl.Extract(InquiryGeneratedSchema.Ddl, "PrimitiveParent", "PrimitiveOptionalParent", "PrimitiveChild");
        Skip.IfNot(_f.IsAvailable, _f.SkipReason);
        await using var h = await MySqlTestHarness.CreateFromDdlAsync(_f.AdminConnectionString, d, "genprimitive");
        await using var c = new MySqlConnection(h.ConnectionString);
        await c.OpenAsync();

        Assert.Equal("ParentId,code", await T(c, "SELECT GROUP_CONCAT(column_name ORDER BY seq_in_index) FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name='PrimitiveChild' AND index_name='IX_PrimitiveChild_Parent_Code'"));
        Assert.Equal("TenantId,code", await T(c, "SELECT GROUP_CONCAT(column_name ORDER BY seq_in_index) FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name='PrimitiveChild' AND index_name='UX_PrimitiveChild_Tenant_Code' AND non_unique=0"));
        Assert.Equal(1, await S(c, "SELECT COUNT(*) FROM information_schema.table_constraints WHERE constraint_schema=DATABASE() AND table_name='PrimitiveChild' AND constraint_name='CK_PrimitiveChild_Quantity' AND constraint_type='CHECK'"));
        Assert.Equal(1, await S(c, "SELECT COUNT(*) FROM information_schema.table_constraints WHERE constraint_schema=DATABASE() AND table_name='PrimitiveChild' AND constraint_name='CK_PrimitiveChild_Code' AND constraint_type='CHECK'"));
        Assert.Equal(1, await S(c, "SELECT COUNT(*) FROM information_schema.referential_constraints WHERE constraint_schema=DATABASE() AND constraint_name='FK_PrimitiveChild_Parent' AND delete_rule='CASCADE'"));
        Assert.Equal(1, await S(c, "SELECT COUNT(*) FROM information_schema.referential_constraints WHERE constraint_schema=DATABASE() AND constraint_name='FK_PrimitiveChild_OptionalParent' AND delete_rule='SET NULL'"));

        await Enforce(c);
        await E(c, GeneratedSchemaDdl.Extract(InquiryGeneratedSchema.Ddl, "UpdateCascadeParent", "UpdateCascadeChild"));
        await E(c, "INSERT INTO UpdateCascadeParent VALUES(10); INSERT INTO UpdateCascadeChild VALUES(1,10); UPDATE UpdateCascadeParent SET Id=11 WHERE Id=10");
        Assert.Equal(1, await S(c, "SELECT COUNT(*) FROM UpdateCascadeChild WHERE ParentId=11"));
    }

    private static async Task Enforce(MySqlConnection c)
    {
        await E(c, "INSERT INTO PrimitiveParent VALUES(1); INSERT INTO PrimitiveOptionalParent VALUES(1); INSERT INTO PrimitiveChild VALUES(1,1,1,7,'A',1)");
        await Assert.ThrowsAsync<MySqlException>(() => E(c, "INSERT INTO PrimitiveChild VALUES(2,1,NULL,7,'A',1)"));
        await Assert.ThrowsAsync<MySqlException>(() => E(c, "INSERT INTO PrimitiveChild VALUES(3,1,NULL,8,'B',-1)"));
        await Assert.ThrowsAsync<MySqlException>(() => E(c, "INSERT INTO PrimitiveChild VALUES(4,1,NULL,9,'',1)"));
        await E(c, "DELETE FROM PrimitiveOptionalParent WHERE Id=1");
        Assert.Equal(1, await S(c, "SELECT COUNT(*) FROM PrimitiveChild WHERE OptionalParentId IS NULL"));
        await E(c, "DELETE FROM PrimitiveParent WHERE Id=1");
        Assert.Equal(0, await S(c, "SELECT COUNT(*) FROM PrimitiveChild"));
    }

    private static async Task E(MySqlConnection c, string s)
    {
        await using var x = c.CreateCommand();
        x.CommandText = s;
        await x.ExecuteNonQueryAsync();
    }

    private static async Task<int> S(MySqlConnection c, string s)
    {
        await using var x = c.CreateCommand();
        x.CommandText = s;
        return Convert.ToInt32(await x.ExecuteScalarAsync());
    }

    private static async Task<string?> T(MySqlConnection c, string s)
    {
        await using var x = c.CreateCommand();
        x.CommandText = s;
        return Convert.ToString(await x.ExecuteScalarAsync());
    }
}
