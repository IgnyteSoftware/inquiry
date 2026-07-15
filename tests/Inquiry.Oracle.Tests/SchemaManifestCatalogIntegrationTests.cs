using System.Text.Json;
using Inquiry.Entities;
using Inquiry.FeatureCatalog;
using Inquiry.Generated;
using Inquiry.Oracle.Tests.Fixtures;
using Oracle.ManagedDataAccess.Client;
using Xunit;

namespace Inquiry.Oracle.Tests;

[InquiryTable("ManifestParent")]
public sealed class ManifestParent { [InquiryKey] public int Id { get; set; } }

[InquiryTable("ManifestWidget")]
[InquiryIndex(nameof(Name), Name = "UX_MANIFESTWIDGET_NAME", IsUnique = true)]
[InquiryCheck("score >= 0", Name = "CK_MANIFESTWIDGET_SCORE")]
public sealed class ManifestWidget
{
    [InquiryKey(IsGenerated = true)] public int Id { get; set; }
    [InquiryForeignKey("ManifestParent", "Id", ConstraintName = "FK_MANIFESTWIDGET_PARENT", OnDelete = InquiryReferentialAction.Cascade)] public int ParentId { get; set; }
    [InquiryColumn(Length = 64)] public string Name { get; set; } = string.Empty;
    [InquiryColumn(Length = 128)] public string? Notes { get; set; }
    [InquiryColumn("score", DefaultExpression = "7")] public int Score { get; set; }
    [InquiryColumn(Computed = "score + 1")] public int Total { get; set; }
}

[Collection(OracleCollection.Name)]
public sealed class SchemaManifestCatalogIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public SchemaManifestCatalogIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task GeneratedManifestMatchesLiveCatalog()
    {
        var ddl = GeneratedSchemaDdl.Extract(InquiryGeneratedSchema.Ddl, "ManifestParent", "ManifestWidget");
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, ddl, "manifest");
        await using var connection = new OracleConnection(harness.ConnectionString); await connection.OpenAsync();
        using var manifest = JsonDocument.Parse(InquiryGeneratedSchema.SchemaManifestJson);
        var table = manifest.RootElement.GetProperty("tables").EnumerateArray().Single(x => x.GetProperty("name").GetString() == "ManifestWidget");
        var expected = table.GetProperty("columns").EnumerateArray().ToArray();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT c.COLUMN_NAME, c.DATA_TYPE || CASE WHEN c.DATA_TYPE IN ('VARCHAR2','NVARCHAR2','CHAR','NCHAR') THEN '(' || c.CHAR_LENGTH || ')' WHEN c.DATA_TYPE='NUMBER' THEN '(' || c.DATA_PRECISION || CASE WHEN c.DATA_SCALE > 0 THEN ',' || c.DATA_SCALE ELSE '' END || ')' ELSE '' END, c.NULLABLE, (pk.POSITION-1) FROM USER_TAB_COLUMNS c LEFT JOIN (SELECT cc.TABLE_NAME,cc.COLUMN_NAME,cc.POSITION FROM USER_CONSTRAINTS con JOIN USER_CONS_COLUMNS cc ON cc.CONSTRAINT_NAME=con.CONSTRAINT_NAME WHERE con.CONSTRAINT_TYPE='P') pk ON pk.TABLE_NAME=c.TABLE_NAME AND pk.COLUMN_NAME=c.COLUMN_NAME WHERE c.TABLE_NAME='MANIFESTWIDGET' ORDER BY c.COLUMN_ID";
        await using var reader = await command.ExecuteReaderAsync();
        var ordinal = 0;
        while (await reader.ReadAsync())
        {
            var column = expected[ordinal++];
            Assert.Equal(column.GetProperty("name").GetString(), reader.GetString(0), ignoreCase: true);
            if (column.GetProperty("storeType").ValueKind != JsonValueKind.Null)
                Assert.Equal(column.GetProperty("storeType").GetString(), reader.GetString(1), ignoreCase: true);
            Assert.Equal(column.GetProperty("nullable").GetBoolean(), reader.GetString(2) == "Y");
            var pk = column.GetProperty("primaryKeyOrdinal");
            Assert.Equal(pk.ValueKind == JsonValueKind.Null ? (int?)null : pk.GetInt32(), reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3));
        }
        await reader.DisposeAsync();
        Assert.Equal(expected.Length, ordinal);
        Assert.Equal(new[] { "Id" }, table.GetProperty("primaryKey").EnumerateArray().Select(x => x.GetString()));
        var columns = table.GetProperty("columns").EnumerateArray().ToDictionary(x => x.GetProperty("name").GetString()!);
        Assert.Equal("identity", columns["Id"].GetProperty("generation").GetString()); Assert.Equal("7", columns["score"].GetProperty("defaultExpression").GetString());
        Assert.Equal("score + 1", columns["Total"].GetProperty("computedExpression").GetString());
        var index = Assert.Single(table.GetProperty("indexes").EnumerateArray()); Assert.True(index.GetProperty("unique").GetBoolean());
        Assert.Equal(new[] { "Name" }, index.GetProperty("keyColumns").EnumerateArray().Select(x => x.GetString())); Assert.Empty(index.GetProperty("includeColumns").EnumerateArray());
        Assert.Equal("CK_MANIFESTWIDGET_SCORE", Assert.Single(table.GetProperty("checks").EnumerateArray()).GetProperty("name").GetString());
        var foreignKey = Assert.Single(table.GetProperty("foreignKeys").EnumerateArray()); Assert.Equal("FK_MANIFESTWIDGET_PARENT", foreignKey.GetProperty("name").GetString());
        Assert.Equal(new[] { "ParentId" }, foreignKey.GetProperty("localColumns").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(JsonValueKind.Null, foreignKey.GetProperty("referencedSchema").ValueKind);
        Assert.Equal("ManifestParent", foreignKey.GetProperty("referencedTable").GetString());
        Assert.Equal(new[] { "Id" }, foreignKey.GetProperty("referencedColumns").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("cascade", foreignKey.GetProperty("onDelete").GetString()); Assert.Equal("no-action", foreignKey.GetProperty("onUpdate").GetString());
        await using var facts = connection.CreateCommand();
        // DATA_DEFAULT is a legacy LONG and cannot participate in SQL comparison (ORA-00932), so
        // default/computed expressions are read and compared client-side below.
        facts.CommandText = "SELECT (SELECT count(*) FROM USER_TAB_COLS WHERE TABLE_NAME='MANIFESTWIDGET' AND IDENTITY_COLUMN='YES'), (SELECT count(*) FROM USER_TAB_COLS WHERE TABLE_NAME='MANIFESTWIDGET' AND VIRTUAL_COLUMN='YES'), (SELECT count(*) FROM USER_INDEXES WHERE TABLE_NAME='MANIFESTWIDGET' AND INDEX_NAME='UX_MANIFESTWIDGET_NAME' AND UNIQUENESS='UNIQUE'), (SELECT count(*) FROM USER_CONSTRAINTS WHERE TABLE_NAME='MANIFESTWIDGET' AND CONSTRAINT_NAME='CK_MANIFESTWIDGET_SCORE' AND CONSTRAINT_TYPE='C'), (SELECT count(*) FROM USER_CONSTRAINTS WHERE TABLE_NAME='MANIFESTWIDGET' AND CONSTRAINT_NAME='FK_MANIFESTWIDGET_PARENT' AND CONSTRAINT_TYPE='R' AND DELETE_RULE='CASCADE') FROM DUAL";
        await using var factsReader = await facts.ExecuteReaderAsync(); Assert.True(await factsReader.ReadAsync());
        Assert.Equal(1, factsReader.GetInt32(0)); Assert.Equal(1, factsReader.GetInt32(1)); Assert.Equal(1, factsReader.GetInt32(2));
        Assert.Equal(1, factsReader.GetInt32(3)); Assert.Equal(1, factsReader.GetInt32(4));
        await factsReader.DisposeAsync();

        await using var indexColumns = connection.CreateCommand();
        indexColumns.CommandText = "SELECT c.COLUMN_NAME FROM USER_IND_COLUMNS c WHERE c.TABLE_NAME='MANIFESTWIDGET' AND c.INDEX_NAME='UX_MANIFESTWIDGET_NAME' ORDER BY c.COLUMN_POSITION";
        var catalogKeys = new List<string>(); await using (var indexReader = await indexColumns.ExecuteReaderAsync()) while (await indexReader.ReadAsync()) catalogKeys.Add(indexReader.GetString(0));
        Assert.Equal(index.GetProperty("keyColumns").EnumerateArray().Select(x => x.GetString()), catalogKeys, StringComparer.OrdinalIgnoreCase);

        await using var fkCommand = connection.CreateCommand();
        fkCommand.CommandText = "SELECT lc.COLUMN_NAME, USER, rt.TABLE_NAME, rc.COLUMN_NAME, fk.DELETE_RULE FROM USER_CONSTRAINTS fk JOIN USER_CONS_COLUMNS lc ON lc.CONSTRAINT_NAME=fk.CONSTRAINT_NAME JOIN USER_CONSTRAINTS pk ON pk.CONSTRAINT_NAME=fk.R_CONSTRAINT_NAME JOIN USER_CONS_COLUMNS rc ON rc.CONSTRAINT_NAME=pk.CONSTRAINT_NAME AND rc.POSITION=lc.POSITION JOIN USER_TABLES rt ON rt.TABLE_NAME=pk.TABLE_NAME WHERE fk.TABLE_NAME='MANIFESTWIDGET' AND fk.CONSTRAINT_NAME='FK_MANIFESTWIDGET_PARENT' ORDER BY lc.POSITION";
        await using var fkReader = await fkCommand.ExecuteReaderAsync(); Assert.True(await fkReader.ReadAsync());
        Assert.Equal(Assert.Single(foreignKey.GetProperty("localColumns").EnumerateArray()).GetString(), fkReader.GetString(0), ignoreCase: true);
        Assert.Equal(foreignKey.GetProperty("referencedTable").GetString(), fkReader.GetString(2), ignoreCase: true);
        Assert.Equal(Assert.Single(foreignKey.GetProperty("referencedColumns").EnumerateArray()).GetString(), fkReader.GetString(3), ignoreCase: true);
        Assert.Equal(foreignKey.GetProperty("onDelete").GetString(), fkReader.GetString(4).ToLowerInvariant()); Assert.False(await fkReader.ReadAsync());
        await fkReader.DisposeAsync();
        // Oracle has no foreign-key ON UPDATE clause or catalog field; the provider contract is always no-action.
        Assert.Equal("no-action", foreignKey.GetProperty("onUpdate").GetString());

        await using var checkExpression = connection.CreateCommand();
        checkExpression.CommandText = "SELECT SEARCH_CONDITION_VC FROM USER_CONSTRAINTS WHERE TABLE_NAME='MANIFESTWIDGET' AND CONSTRAINT_NAME='CK_MANIFESTWIDGET_SCORE' AND CONSTRAINT_TYPE='C'";
        Assert.Equal(
            NormalizeSql(Assert.Single(table.GetProperty("checks").EnumerateArray()).GetProperty("expression").GetString()!),
            NormalizeSql((string)(await checkExpression.ExecuteScalarAsync())!));

        await using var expressions = connection.CreateCommand();
        expressions.InitialLONGFetchSize = -1;
        expressions.CommandText = "SELECT COLUMN_NAME, DATA_DEFAULT FROM USER_TAB_COLS WHERE TABLE_NAME='MANIFESTWIDGET' AND COLUMN_NAME IN ('SCORE','TOTAL') ORDER BY COLUMN_ID";
        await using var expressionReader = await expressions.ExecuteReaderAsync();
        var expressionRows = 0;
        while (await expressionReader.ReadAsync())
        {
            var columnName = expressionReader.GetString(0);
            Assert.Equal(expressionRows == 0 ? "SCORE" : "TOTAL", columnName);
            var expectedExpression = columnName == "SCORE" ? columns["score"].GetProperty("defaultExpression").GetString()! : columns["Total"].GetProperty("computedExpression").GetString()!;
            Assert.Equal(NormalizeSql(expectedExpression), NormalizeSql(expressionReader.GetString(1)));
            expressionRows++;
        }
        Assert.Equal(2, expressionRows);
        await expressionReader.DisposeAsync();
    }

    private static string NormalizeSql(string sql)
        => new(sql.Where(character => !char.IsWhiteSpace(character) && character is not '"' and not '(' and not ')').Select(char.ToUpperInvariant).ToArray());
}
