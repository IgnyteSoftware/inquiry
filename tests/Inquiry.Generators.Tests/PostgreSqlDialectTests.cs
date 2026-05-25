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
    }

    [Fact]
    public void NameIsPostgreSql()
    {
        Assert.Equal("PostgreSql", new PostgreSqlInquirySqlDialect().Name);
    }
}
