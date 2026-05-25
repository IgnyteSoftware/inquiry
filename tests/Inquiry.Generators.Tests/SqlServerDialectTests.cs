using Inquiry.Sql;
using Inquiry.SqlServer;

namespace Inquiry.Generators.Tests;

public sealed class SqlServerDialectTests
{
    private static readonly InquirySqlColumn[] _columns = new InquirySqlColumn[]
    {
        new("Key", "Key", isKey: true),
        new("Name", "Name", isKey: false),
        new("IsActive", "IsActive", isKey: false),
    };

    [Fact]
    public void QuoteIdentifierBrackets()
    {
        var dialect = new SqlServerInquirySqlDialect();
        Assert.Equal("[MyTable]", dialect.QuoteIdentifier("MyTable"));
    }

    [Fact]
    public void QuoteIdentifierEscapesEmbeddedClosingBracket()
    {
        var dialect = new SqlServerInquirySqlDialect();
        Assert.Equal("[My]]Table]", dialect.QuoteIdentifier("My]Table"));
    }

    [Fact]
    public void BuildsSelectStatements()
    {
        var statements = new InquirySqlStatementBuilder(new SqlServerInquirySqlDialect()).Build("dbo", "TOrganization", _columns);

        Assert.Equal("SELECT [Key], [Name], [IsActive] FROM [dbo].[TOrganization]", statements.SelectAll);
        Assert.Equal("SELECT [Key], [Name], [IsActive] FROM [dbo].[TOrganization] WHERE [Key] = @key", statements.SelectByKey);
        Assert.Equal("SELECT [Key], [Name], [IsActive] FROM [dbo].[TOrganization] WHERE [IsActive] = @value", statements.SelectByField["IsActive"]);
    }

    [Fact]
    public void BuildsInsertUpdateDeleteStatements()
    {
        var statements = new InquirySqlStatementBuilder(new SqlServerInquirySqlDialect()).Build("dbo", "TOrganization", _columns);

        Assert.Equal("INSERT INTO [dbo].[TOrganization] ([Key], [Name], [IsActive]) VALUES (@Key, @Name, @IsActive)", statements.Insert);
        Assert.Equal("UPDATE [dbo].[TOrganization] SET [Name] = @Name, [IsActive] = @IsActive WHERE [Key] = @Key", statements.Update);
        Assert.Equal("DELETE FROM [dbo].[TOrganization] WHERE [Key] = @key", statements.DeleteByKey);
    }

    [Fact]
    public void BuildsUpsertWithMerge()
    {
        var statements = new InquirySqlStatementBuilder(new SqlServerInquirySqlDialect()).Build("dbo", "TOrganization", _columns);

        Assert.StartsWith("MERGE INTO [dbo].[TOrganization]", statements.Upsert);
        Assert.Contains("WHEN MATCHED THEN UPDATE", statements.Upsert);
        Assert.Contains("WHEN NOT MATCHED THEN INSERT", statements.Upsert);
    }

    [Fact]
    public void BuildsUpsertStatement()
    {
        var statements = new InquirySqlStatementBuilder(new SqlServerInquirySqlDialect()).Build("dbo", "TOrganization", _columns);

        Assert.Equal(
            "MERGE INTO [dbo].[TOrganization] AS target " +
            "USING (SELECT @Key AS k) AS source ON target.[Key] = source.k " +
            "WHEN MATCHED THEN UPDATE SET [Name] = @Name, [IsActive] = @IsActive " +
            "WHEN NOT MATCHED THEN INSERT ([Key], [Name], [IsActive]) VALUES (@Key, @Name, @IsActive);",
            statements.Upsert);
    }

    [Fact]
    public void NameIsSqlServer()
    {
        Assert.Equal("SqlServer", new SqlServerInquirySqlDialect().Name);
    }
}
