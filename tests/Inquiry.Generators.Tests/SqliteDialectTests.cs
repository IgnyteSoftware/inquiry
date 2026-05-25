using Inquiry.Sql;
using Inquiry.Sqlite;

namespace Inquiry.Generators.Tests;

public sealed class SqliteDialectTests
{
    private static readonly InquirySqlColumn[] _columns = new InquirySqlColumn[]
    {
        new("Key", "Key", isKey: true),
        new("Name", "Name", isKey: false),
        new("IsActive", "IsActive", isKey: false),
    };

    private static (SqliteInquirySqlDialect Dialect, InquirySqlBuildContext Context) NewContext(
        string? schema = null,
        string tableName = "TOrganization",
        InquirySqlColumn[]? columns = null)
    {
        var dialect = new SqliteInquirySqlDialect();
        var context = dialect.CreateContext(schema, tableName, columns ?? _columns);
        return (dialect, context);
    }

    [Fact]
    public void QuoteIdentifierDoubleQuotes()
    {
        var dialect = new SqliteInquirySqlDialect();
        Assert.Equal("\"MyTable\"", dialect.QuoteIdentifier("MyTable"));
    }

    [Fact]
    public void QuoteIdentifierEscapesEmbeddedDoubleQuotes()
    {
        var dialect = new SqliteInquirySqlDialect();
        Assert.Equal("\"My\"\"Table\"", dialect.QuoteIdentifier("My\"Table"));
    }

    [Fact]
    public void BuildsSelectStatements()
    {
        var (dialect, ctx) = NewContext();

        Assert.Equal("SELECT \"Key\", \"Name\", \"IsActive\" FROM \"TOrganization\"", dialect.BuildSelectAllSql(ctx));
        Assert.Equal("SELECT \"Key\", \"Name\", \"IsActive\" FROM \"TOrganization\" WHERE \"Key\" = @key", dialect.BuildSelectByKeySql(ctx));
        Assert.Equal("SELECT \"Key\", \"Name\", \"IsActive\" FROM \"TOrganization\" WHERE \"IsActive\" = @value", dialect.BuildSelectByFieldSql(ctx, _columns[2]));
    }

    [Fact]
    public void BuildsInsertUpdateDeleteStatements()
    {
        var (dialect, ctx) = NewContext();

        Assert.Equal("INSERT INTO \"TOrganization\" (\"Key\", \"Name\", \"IsActive\") VALUES (@Key, @Name, @IsActive)", dialect.BuildInsertSql(ctx));
        Assert.Equal("INSERT INTO \"TOrganization\" (\"Key\", \"Name\", \"IsActive\") VALUES (@Key, @Name, @IsActive) RETURNING \"Key\", \"Name\", \"IsActive\"", dialect.BuildInsertReturningSql(ctx));
        Assert.Equal("UPDATE \"TOrganization\" SET \"Name\" = @Name, \"IsActive\" = @IsActive WHERE \"Key\" = @Key", dialect.BuildUpdateSql(ctx));
        Assert.Equal("UPDATE \"TOrganization\" SET \"Name\" = @Name, \"IsActive\" = @IsActive WHERE \"Key\" = @Key RETURNING \"Key\", \"Name\", \"IsActive\"", dialect.BuildUpdateReturningSql(ctx));
        Assert.Equal("DELETE FROM \"TOrganization\" WHERE \"Key\" = @key", dialect.BuildDeleteByKeySql(ctx));
    }

    [Fact]
    public void BuildsUpsertWithInsertOrReplace()
    {
        var (dialect, ctx) = NewContext();

        Assert.StartsWith("INSERT OR REPLACE INTO \"TOrganization\"", dialect.BuildUpsertSql(ctx));
    }

    [Fact]
    public void BuildsUpsertStatement()
    {
        var (dialect, ctx) = NewContext();

        Assert.Equal(
            "INSERT OR REPLACE INTO \"TOrganization\" (\"Key\", \"Name\", \"IsActive\") VALUES (@Key, @Name, @IsActive)",
            dialect.BuildUpsertSql(ctx));
        Assert.Equal(
            "INSERT OR REPLACE INTO \"TOrganization\" (\"Key\", \"Name\", \"IsActive\") VALUES (@Key, @Name, @IsActive) RETURNING \"Key\", \"Name\", \"IsActive\"",
            dialect.BuildUpsertReturningSql(ctx));
    }

    [Fact]
    public void GeneratedKeyIsExcludedFromInsert()
    {
        var columns = new InquirySqlColumn[]
        {
            new("Id", "Id", isKey: true, isGenerated: true),
            new("Name", "Name", isKey: false),
        };
        var (dialect, ctx) = NewContext(tableName: "TItems", columns: columns);

        var insert = dialect.BuildInsertSql(ctx);
        Assert.DoesNotContain("Id", insert);
        Assert.Equal("INSERT INTO \"TItems\" (\"Name\") VALUES (@Name)", insert);
    }

    [Fact]
    public void CreateContextThrowsWhenNoKeyColumn()
    {
        var columns = new InquirySqlColumn[]
        {
            new("Name", "Name", isKey: false),
        };
        var dialect = new SqliteInquirySqlDialect();

        Assert.Throws<ArgumentException>(() => dialect.CreateContext(null, "T", columns));
    }

    [Fact]
    public void BuildInsertThrowsWhenAllColumnsGenerated()
    {
        var columns = new InquirySqlColumn[]
        {
            new("Id", "Id", isKey: true, isGenerated: true),
        };
        var dialect = new SqliteInquirySqlDialect();
        var ctx = dialect.CreateContext(null, "T", columns);

        Assert.Throws<InvalidOperationException>(() => dialect.BuildInsertSql(ctx));
    }

    [Fact]
    public void NameIsSqlite()
    {
        Assert.Equal("Sqlite", new SqliteInquirySqlDialect().Name);
    }

    [Fact]
    public void SchemaPrefixIsRenderedWhenProvided()
    {
        var (dialect, ctx) = NewContext(schema: "main");

        Assert.Equal("SELECT \"Key\", \"Name\", \"IsActive\" FROM \"main\".\"TOrganization\"", dialect.BuildSelectAllSql(ctx));
        Assert.Equal("DELETE FROM \"main\".\"TOrganization\" WHERE \"Key\" = @key", dialect.BuildDeleteByKeySql(ctx));
    }

    [Fact]
    public void BuildSelectByFieldSqlEscapesDoubleQuotesInColumnName()
    {
        var (dialect, ctx) = NewContext();
        var weird = new InquirySqlColumn("Weird", "Wei\"rd", isKey: false);

        Assert.Equal(
            "SELECT \"Key\", \"Name\", \"IsActive\" FROM \"TOrganization\" WHERE \"Wei\"\"rd\" = @value",
            dialect.BuildSelectByFieldSql(ctx, weird));
    }

    [Fact]
    public void UpsertThrowsWhenKeyIsGenerated()
    {
        var columns = new InquirySqlColumn[]
        {
            new("Id", "Id", isKey: true, isGenerated: true),
            new("Name", "Name", isKey: false),
        };
        var (dialect, ctx) = NewContext(tableName: "TItems", columns: columns);

        Assert.Throws<InvalidOperationException>(() => dialect.BuildUpsertSql(ctx));
    }
}
