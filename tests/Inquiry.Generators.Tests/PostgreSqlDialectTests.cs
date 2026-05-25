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
        var statements = new InquirySqlStatementBuilder(new PostgreSqlInquirySqlDialect()).Build(null, "TOrganization", _columns);

        Assert.Equal("SELECT \"Key\", \"Name\", \"IsActive\" FROM \"TOrganization\"", statements.SelectAll);
        Assert.Equal("SELECT \"Key\", \"Name\", \"IsActive\" FROM \"TOrganization\" WHERE \"Key\" = @key", statements.SelectByKey);
    }

    [Fact]
    public void BuildsUpsertWithOnConflict()
    {
        var statements = new InquirySqlStatementBuilder(new PostgreSqlInquirySqlDialect()).Build(null, "TOrganization", _columns);

        Assert.Contains("ON CONFLICT", statements.Upsert);
        Assert.Contains("DO UPDATE SET", statements.Upsert);
        Assert.StartsWith("INSERT INTO \"TOrganization\"", statements.Upsert);
    }

    [Fact]
    public void BuildsUpsertStatement()
    {
        var statements = new InquirySqlStatementBuilder(new PostgreSqlInquirySqlDialect()).Build(null, "TOrganization", _columns);

        Assert.Equal(
            "INSERT INTO \"TOrganization\" (\"Key\", \"Name\", \"IsActive\") VALUES (@Key, @Name, @IsActive) " +
            "ON CONFLICT (\"Key\") DO UPDATE SET \"Name\" = @Name, \"IsActive\" = @IsActive",
            statements.Upsert);
    }

    [Fact]
    public void NameIsPostgreSql()
    {
        Assert.Equal("PostgreSql", new PostgreSqlInquirySqlDialect().Name);
    }
}
