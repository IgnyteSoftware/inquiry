using Inquiry.Sql;
using Inquiry.Sqlite;
using Inquiry.SqlServer;

namespace Inquiry.Generators.Tests;

public sealed class SqlStatementBuilderTests
{
    private static readonly InquirySqlColumn[] _orgColumns = new InquirySqlColumn[]
    {
        new("Key", "Key", isKey: true),
        new("Name", "Name", isKey: false),
        new("IsActive", "IsActive", isKey: false),
    };

    [Fact]
    public void BuildsSqlServerCrudStatements()
    {
        var statements = new InquirySqlStatementBuilder(new SqlServerInquirySqlDialect()).Build(
            schema: "dbo",
            tableName: "TOrganization",
            columns: _orgColumns);

        Assert.Equal("SELECT [Key], [Name], [IsActive] FROM [dbo].[TOrganization]", statements.SelectAll);
        Assert.Equal("SELECT [Key], [Name], [IsActive] FROM [dbo].[TOrganization] WHERE [Key] = @key", statements.SelectByKey);
        Assert.Equal("INSERT INTO [dbo].[TOrganization] ([Key], [Name], [IsActive]) VALUES (@Key, @Name, @IsActive)", statements.Insert);
        Assert.Equal("UPDATE [dbo].[TOrganization] SET [Name] = @Name, [IsActive] = @IsActive WHERE [Key] = @Key", statements.Update);
        Assert.Equal("DELETE FROM [dbo].[TOrganization] WHERE [Key] = @key", statements.DeleteByKey);
    }

    [Fact]
    public void BuildsSqliteCrudStatements()
    {
        var statements = new InquirySqlStatementBuilder(new SqliteInquirySqlDialect()).Build(
            schema: null,
            tableName: "TOrganization",
            columns: _orgColumns);

        Assert.Equal("SELECT \"Key\", \"Name\", \"IsActive\" FROM \"TOrganization\"", statements.SelectAll);
        Assert.Equal("SELECT \"Key\", \"Name\", \"IsActive\" FROM \"TOrganization\" WHERE \"Key\" = @key", statements.SelectByKey);
        Assert.Equal("INSERT INTO \"TOrganization\" (\"Key\", \"Name\", \"IsActive\") VALUES (@Key, @Name, @IsActive)", statements.Insert);
        Assert.Equal("UPDATE \"TOrganization\" SET \"Name\" = @Name, \"IsActive\" = @IsActive WHERE \"Key\" = @Key", statements.Update);
        Assert.Equal("DELETE FROM \"TOrganization\" WHERE \"Key\" = @key", statements.DeleteByKey);
        Assert.Equal("SELECT \"Key\", \"Name\", \"IsActive\" FROM \"TOrganization\" WHERE \"IsActive\" = @value", statements.SelectByField["IsActive"]);
    }

    [Fact]
    public void BuildsSqliteUpsertStatement()
    {
        var statements = new InquirySqlStatementBuilder(new SqliteInquirySqlDialect()).Build(null, "TOrganization", _orgColumns);

        Assert.Equal(
            "INSERT OR REPLACE INTO \"TOrganization\" (\"Key\", \"Name\", \"IsActive\") VALUES (@Key, @Name, @IsActive)",
            statements.Upsert);
    }

    [Fact]
    public void BuildsSqlServerUpsertStatement()
    {
        var statements = new InquirySqlStatementBuilder(new SqlServerInquirySqlDialect()).Build("dbo", "TOrganization", _orgColumns);

        Assert.StartsWith("MERGE INTO [dbo].[TOrganization]", statements.Upsert);
        Assert.Contains("WHEN MATCHED THEN UPDATE", statements.Upsert);
        Assert.Contains("WHEN NOT MATCHED THEN INSERT", statements.Upsert);
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

        // Generated key should not appear in insert columns/parameters
        Assert.DoesNotContain("Id", statements.Insert);
        Assert.Equal("INSERT INTO \"TItems\" (\"Name\") VALUES (@Name)", statements.Insert);
    }

    [Fact]
    public void SelectByFieldCoversAllColumns()
    {
        var statements = new InquirySqlStatementBuilder(new SqliteInquirySqlDialect()).Build(null, "TOrganization", _orgColumns);

        Assert.True(statements.SelectByField.ContainsKey("Key"));
        Assert.True(statements.SelectByField.ContainsKey("Name"));
        Assert.True(statements.SelectByField.ContainsKey("IsActive"));
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
}
