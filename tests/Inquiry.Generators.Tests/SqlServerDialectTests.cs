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

    private static (SqlServerInquirySqlDialect Dialect, InquirySqlBuildContext Context) NewContext(string? schema = "dbo")
    {
        var dialect = new SqlServerInquirySqlDialect();
        var context = dialect.CreateContext(schema, "TOrganization", _columns);
        return (dialect, context);
    }

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
        var (dialect, ctx) = NewContext();

        Assert.Equal("SELECT [Key], [Name], [IsActive] FROM [dbo].[TOrganization]", dialect.BuildSelectAllSql(ctx));
        Assert.Equal("SELECT [Key], [Name], [IsActive] FROM [dbo].[TOrganization] WHERE [Key] = @key", dialect.BuildSelectByKeySql(ctx));
        Assert.Equal("SELECT [Key], [Name], [IsActive] FROM [dbo].[TOrganization] WHERE [IsActive] = @value", dialect.BuildSelectByFieldSql(ctx, _columns[2]));
    }

    [Fact]
    public void BuildsInsertUpdateDeleteStatements()
    {
        var (dialect, ctx) = NewContext();

        Assert.Equal("INSERT INTO [dbo].[TOrganization] ([Key], [Name], [IsActive]) VALUES (@Key, @Name, @IsActive)", dialect.BuildInsertSql(ctx));
        Assert.Equal("UPDATE [dbo].[TOrganization] SET [Name] = @Name, [IsActive] = @IsActive WHERE [Key] = @Key", dialect.BuildUpdateSql(ctx));
        Assert.Equal("DELETE FROM [dbo].[TOrganization] WHERE [Key] = @key", dialect.BuildDeleteByKeySql(ctx));
    }

    [Fact]
    public void BuildsUpsertWithMerge()
    {
        var (dialect, ctx) = NewContext();
        var upsert = dialect.BuildUpsertSql(ctx);

        Assert.StartsWith("MERGE INTO [dbo].[TOrganization]", upsert);
        Assert.Contains("WHEN MATCHED THEN UPDATE", upsert);
        Assert.Contains("WHEN NOT MATCHED THEN INSERT", upsert);
    }

    [Fact]
    public void BuildsUpsertStatement()
    {
        var (dialect, ctx) = NewContext();

        Assert.Equal(
            "MERGE INTO [dbo].[TOrganization] AS target " +
            "USING (SELECT @Key AS k) AS source ON target.[Key] = source.k " +
            "WHEN MATCHED THEN UPDATE SET [Name] = @Name, [IsActive] = @IsActive " +
            "WHEN NOT MATCHED THEN INSERT ([Key], [Name], [IsActive]) VALUES (@Key, @Name, @IsActive);",
            dialect.BuildUpsertSql(ctx));
    }

    [Fact]
    public void NameIsSqlServer()
    {
        Assert.Equal("SqlServer", new SqlServerInquirySqlDialect().Name);
    }

    [Fact]
    public void SchemaIsOmittedWhenNull()
    {
        var (dialect, ctx) = NewContext(schema: null);

        Assert.Equal("SELECT [Key], [Name], [IsActive] FROM [TOrganization]", dialect.BuildSelectAllSql(ctx));
    }

    [Fact]
    public void UpsertThrowsWhenKeyIsGenerated()
    {
        var columns = new InquirySqlColumn[]
        {
            new("Id", "Id", isKey: true, isGenerated: true),
            new("Name", "Name", isKey: false),
        };
        var dialect = new SqlServerInquirySqlDialect();
        var ctx = dialect.CreateContext("dbo", "TItems", columns);

        Assert.Throws<InvalidOperationException>(() => dialect.BuildUpsertSql(ctx));
    }
}
