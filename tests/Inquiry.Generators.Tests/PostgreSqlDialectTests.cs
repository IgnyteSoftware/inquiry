using Inquiry.PostgreSql;
using Inquiry.Sql;

namespace Inquiry.Generators.Tests;

public sealed class PostgreSqlDialectTests
{
    private static readonly InquirySqlColumn[] _columns = new InquirySqlColumn[]
    {
        new("Key", "Key", isKey: true),
        new("Name", "Name", isKey: false),
        new("IsActive", "IsActive", isKey: false),
    };

    private static (PostgreSqlInquirySqlDialect Dialect, InquirySqlBuildContext Context) NewContext(string? schema = null)
    {
        var dialect = new PostgreSqlInquirySqlDialect();
        var context = dialect.CreateContext(schema, "TOrganization", _columns);
        return (dialect, context);
    }

    [Fact]
    public void QuoteIdentifierDoubleQuotes()
    {
        var dialect = new PostgreSqlInquirySqlDialect();
        Assert.Equal("\"MyTable\"", dialect.QuoteIdentifier("MyTable"));
    }

    [Fact]
    public void QuoteIdentifierEscapesEmbeddedDoubleQuotes()
    {
        var dialect = new PostgreSqlInquirySqlDialect();
        Assert.Equal("\"My\"\"Table\"", dialect.QuoteIdentifier("My\"Table"));
    }

    [Fact]
    public void BuildsSelectStatements()
    {
        var (dialect, ctx) = NewContext();

        Assert.Equal("SELECT \"Key\", \"Name\", \"IsActive\" FROM \"TOrganization\"", dialect.BuildSelectAllSql(ctx));
        Assert.Equal("SELECT \"Key\", \"Name\", \"IsActive\" FROM \"TOrganization\" WHERE \"Key\" = @key", dialect.BuildSelectByKeySql(ctx));
    }

    [Fact]
    public void BuildsUpsertWithOnConflict()
    {
        var (dialect, ctx) = NewContext();
        var upsert = dialect.BuildUpsertSql(ctx);

        Assert.Contains("ON CONFLICT", upsert);
        Assert.Contains("DO UPDATE SET", upsert);
        Assert.StartsWith("INSERT INTO \"TOrganization\"", upsert);
    }

    [Fact]
    public void BuildsUpsertStatement()
    {
        var (dialect, ctx) = NewContext();

        Assert.Equal(
            "INSERT INTO \"TOrganization\" (\"Key\", \"Name\", \"IsActive\") VALUES (@Key, @Name, @IsActive) " +
            "ON CONFLICT (\"Key\") DO UPDATE SET \"Name\" = @Name, \"IsActive\" = @IsActive",
            dialect.BuildUpsertSql(ctx));
        Assert.Equal(
            "INSERT INTO \"TOrganization\" (\"Key\", \"Name\", \"IsActive\") VALUES (@Key, @Name, @IsActive) " +
            "ON CONFLICT (\"Key\") DO UPDATE SET \"Name\" = @Name, \"IsActive\" = @IsActive RETURNING \"Key\", \"Name\", \"IsActive\"",
            dialect.BuildUpsertReturningSql(ctx));
    }

    [Fact]
    public void NameIsPostgreSql()
    {
        Assert.Equal("PostgreSql", new PostgreSqlInquirySqlDialect().Name);
    }

    [Fact]
    public void SchemaPrefixIsRenderedWhenProvided()
    {
        var (dialect, ctx) = NewContext(schema: "public");

        Assert.Equal("SELECT \"Key\", \"Name\", \"IsActive\" FROM \"public\".\"TOrganization\"", dialect.BuildSelectAllSql(ctx));
        Assert.Equal("DELETE FROM \"public\".\"TOrganization\" WHERE \"Key\" = @key", dialect.BuildDeleteByKeySql(ctx));
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
    public void BuildSelectByFieldSqlFiltersOnArbitraryColumn()
    {
        var (dialect, ctx) = NewContext();

        Assert.Equal(
            "SELECT \"Key\", \"Name\", \"IsActive\" FROM \"TOrganization\" WHERE \"IsActive\" = @value",
            dialect.BuildSelectByFieldSql(ctx, _columns[2]));
    }

    [Fact]
    public void BuildsGeneratedKeyUpsertStatement()
    {
        var columns = new InquirySqlColumn[]
        {
            new("Id", "Id", isKey: true, isGenerated: true),
            new("Name", "Name", isKey: false),
        };
        var dialect = new PostgreSqlInquirySqlDialect();
        var ctx = dialect.CreateContext(null, "TItems", columns);

        var upsert = dialect.BuildUpsertSql(ctx);
        var returning = dialect.BuildUpsertReturningSql(ctx);

        Assert.StartsWith("UPDATE \"TItems\" SET \"Name\" = @Name", upsert);
        Assert.Contains("INSERT INTO \"TItems\" (\"Name\") SELECT @Name WHERE @Id IS NULL", upsert);
        Assert.Contains("INSERT INTO \"TItems\" (\"Id\", \"Name\") SELECT @Id, @Name", upsert);
        Assert.Contains("WITH updated AS", returning);
        Assert.Contains("SELECT \"Id\", \"Name\" FROM updated", returning);
    }
}
