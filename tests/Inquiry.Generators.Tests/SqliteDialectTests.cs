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
        var statements = new InquirySqlStatementBuilder(new SqliteInquirySqlDialect()).Build(null, "TOrganization", _columns);

        Assert.Equal("SELECT \"Key\", \"Name\", \"IsActive\" FROM \"TOrganization\"", statements.SelectAll);
        Assert.Equal("SELECT \"Key\", \"Name\", \"IsActive\" FROM \"TOrganization\" WHERE \"Key\" = @key", statements.SelectByKey);
        Assert.Equal("SELECT \"Key\", \"Name\", \"IsActive\" FROM \"TOrganization\" WHERE \"IsActive\" = @value", statements.SelectByField["IsActive"]);
    }

    [Fact]
    public void BuildsInsertUpdateDeleteStatements()
    {
        var statements = new InquirySqlStatementBuilder(new SqliteInquirySqlDialect()).Build(null, "TOrganization", _columns);

        Assert.Equal("INSERT INTO \"TOrganization\" (\"Key\", \"Name\", \"IsActive\") VALUES (@Key, @Name, @IsActive)", statements.Insert);
        Assert.Equal("UPDATE \"TOrganization\" SET \"Name\" = @Name, \"IsActive\" = @IsActive WHERE \"Key\" = @Key", statements.Update);
        Assert.Equal("DELETE FROM \"TOrganization\" WHERE \"Key\" = @key", statements.DeleteByKey);
    }

    [Fact]
    public void BuildsUpsertWithInsertOrReplace()
    {
        var statements = new InquirySqlStatementBuilder(new SqliteInquirySqlDialect()).Build(null, "TOrganization", _columns);

        Assert.StartsWith("INSERT OR REPLACE INTO \"TOrganization\"", statements.Upsert);
    }

    [Fact]
    public void BuildsUpsertStatement()
    {
        var statements = new InquirySqlStatementBuilder(new SqliteInquirySqlDialect()).Build(null, "TOrganization", _columns);

        Assert.Equal(
            "INSERT OR REPLACE INTO \"TOrganization\" (\"Key\", \"Name\", \"IsActive\") VALUES (@Key, @Name, @IsActive)",
            statements.Upsert);
    }

    [Fact]
    public void SelectByFieldCoversAllColumns()
    {
        var statements = new InquirySqlStatementBuilder(new SqliteInquirySqlDialect()).Build(null, "TOrganization", _columns);

        Assert.True(statements.SelectByField.ContainsKey("Key"));
        Assert.True(statements.SelectByField.ContainsKey("Name"));
        Assert.True(statements.SelectByField.ContainsKey("IsActive"));
    }

    [Fact]
    public void GeneratedKeyIsExcludedFromInsertAndUpsert()
    {
        var columns = new InquirySqlColumn[]
        {
            new("Id", "Id", isKey: true, isGenerated: true),
            new("Name", "Name", isKey: false),
        };

        var statements = new InquirySqlStatementBuilder(new SqliteInquirySqlDialect()).Build(null, "TItems", columns);

        Assert.DoesNotContain("Id", statements.Insert);
        Assert.Equal("INSERT INTO \"TItems\" (\"Name\") VALUES (@Name)", statements.Insert);
    }

    [Fact]
    public void BuildThrowsWhenNoKeyColumn()
    {
        var columns = new InquirySqlColumn[]
        {
            new("Name", "Name", isKey: false),
        };

        Assert.Throws<ArgumentException>(() =>
            new InquirySqlStatementBuilder(new SqliteInquirySqlDialect()).Build(null, "T", columns));
    }

    [Fact]
    public void BuildThrowsWhenAllColumnsGenerated()
    {
        var columns = new InquirySqlColumn[]
        {
            new("Id", "Id", isKey: true, isGenerated: true),
        };

        Assert.Throws<ArgumentException>(() =>
            new InquirySqlStatementBuilder(new SqliteInquirySqlDialect()).Build(null, "T", columns));
    }

    [Fact]
    public void NameIsSqlite()
    {
        Assert.Equal("Sqlite", new SqliteInquirySqlDialect().Name);
    }
}
