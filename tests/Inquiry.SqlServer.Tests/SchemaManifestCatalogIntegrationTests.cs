using System.Text.Json;
using Inquiry.Entities;
using Inquiry.FeatureCatalog;
using Inquiry.Generated;
using Inquiry.SqlServer.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Inquiry.SqlServer.Tests;

[InquiryTable("ManifestParent")]
public sealed class ManifestParent
{
    [InquiryKey] public int Id { get; set; }
}

[InquiryTable("ManifestWidget")]
[InquiryIndex(nameof(Name), Name = "UX_ManifestWidget_Name", IsUnique = true, Include = [nameof(Notes)])]
[InquiryCheck("score >= 0", Name = "CK_ManifestWidget_Score")]
public sealed class ManifestWidget
{
    [InquiryKey(IsGenerated = true)] public int Id { get; set; }
    [InquiryForeignKey("ManifestParent", "Id", ConstraintName = "FK_ManifestWidget_Parent", OnDelete = InquiryReferentialAction.Cascade)]
    public int ParentId { get; set; }
    [InquiryColumn(Length = 64)] public string Name { get; set; } = string.Empty;
    [InquiryColumn(Length = 128)] public string? Notes { get; set; }
    [InquiryColumn("score", DefaultExpression = "7")] public int Score { get; set; }
    [InquiryColumn(Computed = "score + 1")] public int Total { get; set; }
}

[Collection(SqlServerCollection.Name)]
public sealed class SchemaManifestCatalogIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public SchemaManifestCatalogIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task GeneratedManifestMatchesLiveCatalog()
    {
        var ddl = GeneratedSchemaDdl.Extract(InquiryGeneratedSchema.Ddl, "ManifestParent", "ManifestWidget");
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, ddl, "manifest", false);
        await using var connection = new SqlConnection(harness.ConnectionString);
        await connection.OpenAsync();
        using var manifest = JsonDocument.Parse(InquiryGeneratedSchema.SchemaManifestJson);
        var table = manifest.RootElement.GetProperty("tables").EnumerateArray().Single(x => x.GetProperty("name").GetString() == "ManifestWidget");
        var expected = table.GetProperty("columns").EnumerateArray().ToArray();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.name,
              ty.name + CASE WHEN ty.name IN ('nvarchar','nchar') THEN '(' + CONVERT(varchar(10), c.max_length / 2) + ')' WHEN ty.name IN ('varchar','char','varbinary','binary') THEN '(' + CONVERT(varchar(10), c.max_length) + ')' ELSE '' END,
              c.is_nullable, ic.key_ordinal - 1
            FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id
            LEFT JOIN sys.indexes i ON i.object_id=t.object_id AND i.is_primary_key=1
            LEFT JOIN sys.index_columns ic ON ic.object_id=t.object_id AND ic.index_id=i.index_id AND ic.column_id=c.column_id
            WHERE t.name='ManifestWidget' ORDER BY c.column_id
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var ordinal = 0;
        while (await reader.ReadAsync())
        {
            var column = expected[ordinal++];
            Assert.Equal(column.GetProperty("name").GetString(), reader.GetString(0));
            if (column.GetProperty("storeType").ValueKind != JsonValueKind.Null)
                Assert.Equal(column.GetProperty("storeType").GetString(), reader.GetString(1), ignoreCase: true);
            Assert.Equal(column.GetProperty("nullable").GetBoolean(), reader.GetBoolean(2));
            var pk = column.GetProperty("primaryKeyOrdinal");
            Assert.Equal(pk.ValueKind == JsonValueKind.Null ? (int?)null : pk.GetInt32(), reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3));
        }
        await reader.DisposeAsync();
        Assert.Equal(expected.Length, ordinal);
        Assert.Equal(new[] { "Id" }, table.GetProperty("primaryKey").EnumerateArray().Select(x => x.GetString()));

        var columns = table.GetProperty("columns").EnumerateArray().ToDictionary(x => x.GetProperty("name").GetString()!);
        Assert.Equal("identity", columns["Id"].GetProperty("generation").GetString());
        Assert.Equal("default", columns["score"].GetProperty("generation").GetString());
        Assert.Equal("7", columns["score"].GetProperty("defaultExpression").GetString());
        Assert.Equal("computed", columns["Total"].GetProperty("generation").GetString());
        Assert.Equal("score + 1", columns["Total"].GetProperty("computedExpression").GetString());

        await using var facts = connection.CreateCommand();
        facts.CommandText = """
            SELECT
              COLUMNPROPERTY(t.object_id, 'Id', 'IsIdentity'), dc.definition, cc.definition,
              i.is_unique, ic.is_included_column,
              chk.name, fk.name, pc.name, OBJECT_SCHEMA_NAME(fkc.referenced_object_id), OBJECT_NAME(fkc.referenced_object_id), rc.name,
              fk.delete_referential_action_desc, fk.update_referential_action_desc
            FROM sys.tables t
            JOIN sys.columns c ON c.object_id=t.object_id AND c.name='score'
            LEFT JOIN sys.default_constraints dc ON dc.parent_object_id=c.object_id AND dc.parent_column_id=c.column_id
            LEFT JOIN sys.computed_columns cc ON cc.object_id=t.object_id AND cc.name='Total'
            JOIN sys.indexes i ON i.object_id=t.object_id AND i.name='UX_ManifestWidget_Name'
            JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
            JOIN sys.columns inc ON inc.object_id=ic.object_id AND inc.column_id=ic.column_id AND inc.name='Notes'
            JOIN sys.check_constraints chk ON chk.parent_object_id=t.object_id AND chk.name='CK_ManifestWidget_Score'
            JOIN sys.foreign_keys fk ON fk.parent_object_id=t.object_id AND fk.name='FK_ManifestWidget_Parent'
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
            JOIN sys.columns pc ON pc.object_id=fkc.parent_object_id AND pc.column_id=fkc.parent_column_id
            JOIN sys.columns rc ON rc.object_id=fkc.referenced_object_id AND rc.column_id=fkc.referenced_column_id
            WHERE t.name='ManifestWidget'
            """;
        await using var factReader = await facts.ExecuteReaderAsync();
        Assert.True(await factReader.ReadAsync());
        Assert.Equal(1, factReader.GetInt32(0));
        Assert.Equal("((7))", factReader.GetString(1));
        Assert.Contains("Score", factReader.GetString(2), StringComparison.OrdinalIgnoreCase);
        Assert.True(factReader.GetBoolean(3));
        Assert.True(factReader.GetBoolean(4));
        Assert.Equal("CK_ManifestWidget_Score", factReader.GetString(5));
        Assert.Equal("FK_ManifestWidget_Parent", factReader.GetString(6));
        Assert.Equal("ParentId", factReader.GetString(7));
        Assert.Equal("dbo", factReader.GetString(8));
        Assert.Equal("ManifestParent", factReader.GetString(9));
        Assert.Equal("Id", factReader.GetString(10));
        Assert.Equal("CASCADE", factReader.GetString(11));
        Assert.Equal("NO_ACTION", factReader.GetString(12));
        await factReader.DisposeAsync();

        var index = Assert.Single(table.GetProperty("indexes").EnumerateArray());
        Assert.True(index.GetProperty("unique").GetBoolean());
        Assert.Equal(new[] { "Name" }, index.GetProperty("keyColumns").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(new[] { "Notes" }, index.GetProperty("includeColumns").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("CK_ManifestWidget_Score", Assert.Single(table.GetProperty("checks").EnumerateArray()).GetProperty("name").GetString());
        var foreignKey = Assert.Single(table.GetProperty("foreignKeys").EnumerateArray());
        Assert.Equal("FK_ManifestWidget_Parent", foreignKey.GetProperty("name").GetString());
        Assert.Equal(new[] { "ParentId" }, foreignKey.GetProperty("localColumns").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(JsonValueKind.Null, foreignKey.GetProperty("referencedSchema").ValueKind);
        Assert.Equal("ManifestParent", foreignKey.GetProperty("referencedTable").GetString());
        Assert.Equal(new[] { "Id" }, foreignKey.GetProperty("referencedColumns").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("cascade", foreignKey.GetProperty("onDelete").GetString());
        Assert.Equal("no-action", foreignKey.GetProperty("onUpdate").GetString());

        await using var checkExpression = connection.CreateCommand();
        checkExpression.CommandText = "SELECT chk.definition FROM sys.check_constraints chk JOIN sys.tables t ON t.object_id=chk.parent_object_id WHERE t.name='ManifestWidget' AND chk.name='CK_ManifestWidget_Score'";
        Assert.Equal(
            NormalizeCheckExpression(Assert.Single(table.GetProperty("checks").EnumerateArray()).GetProperty("expression").GetString()!),
            NormalizeCheckExpression((string)(await checkExpression.ExecuteScalarAsync())!));

        await using var indexColumns = connection.CreateCommand();
        indexColumns.CommandText = "SELECT c.name, ic.is_included_column FROM sys.tables t JOIN sys.indexes i ON i.object_id=t.object_id JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE t.name='ManifestWidget' AND i.name='UX_ManifestWidget_Name' ORDER BY CASE WHEN ic.is_included_column=0 THEN ic.key_ordinal ELSE 2147483647 END, ic.index_column_id";
        await using var indexReader = await indexColumns.ExecuteReaderAsync();
        var catalogKeys = new List<string>(); var catalogIncludes = new List<string>();
        while (await indexReader.ReadAsync()) (indexReader.GetBoolean(1) ? catalogIncludes : catalogKeys).Add(indexReader.GetString(0));
        Assert.Equal(index.GetProperty("keyColumns").EnumerateArray().Select(x => x.GetString()), catalogKeys);
        Assert.Equal(index.GetProperty("includeColumns").EnumerateArray().Select(x => x.GetString()), catalogIncludes);
    }

    // Catalogs add only identifier delimiters, whitespace, and wrapper parentheses around this simple predicate.
    private static string NormalizeCheckExpression(string expression)
        => new(expression.Where(character => !char.IsWhiteSpace(character) && character is not '[' and not ']' and not '(' and not ')').Select(char.ToUpperInvariant).ToArray());
}
