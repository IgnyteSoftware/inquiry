using Inquiry.Generators.Sql;

namespace Inquiry.Generators.Tests;

public sealed class SqlStatementBuilderTests
{
    [Fact]
    public void BuildsSqlServerCrudStatements()
    {
        var statements = new SqlStatementBuilder(SqlServerSqlDialect.Instance).Build(
            schema: "dbo",
            tableName: "TOrganization",
            columns: new[]
            {
                new SqlColumn("Key", "Key", isKey: true),
                new SqlColumn("Name", "Name", isKey: false),
                new SqlColumn("IsActive", "IsActive", isKey: false),
            });

        Assert.Equal("SELECT [Key], [Name], [IsActive] FROM [dbo].[TOrganization]", statements.SelectAll);
        Assert.Equal("SELECT [Key], [Name], [IsActive] FROM [dbo].[TOrganization] WHERE [Key] = @key", statements.SelectByKey);
        Assert.Equal("INSERT INTO [dbo].[TOrganization] ([Key], [Name], [IsActive]) VALUES (@Key, @Name, @IsActive)", statements.Insert);
        Assert.Equal("UPDATE [dbo].[TOrganization] SET [Name] = @Name, [IsActive] = @IsActive WHERE [Key] = @Key", statements.Update);
        Assert.Equal("DELETE FROM [dbo].[TOrganization] WHERE [Key] = @key", statements.DeleteByKey);
    }

    [Fact]
    public void BuildsSqliteCrudStatements()
    {
        var statements = new SqlStatementBuilder(SqliteSqlDialect.Instance).Build(
            schema: null,
            tableName: "TOrganization",
            columns: new[]
            {
                new SqlColumn("Key", "Key", isKey: true),
                new SqlColumn("Name", "Name", isKey: false),
                new SqlColumn("IsActive", "IsActive", isKey: false),
            });

        Assert.Equal("SELECT \"Key\", \"Name\", \"IsActive\" FROM \"TOrganization\"", statements.SelectAll);
        Assert.Equal("SELECT \"Key\", \"Name\", \"IsActive\" FROM \"TOrganization\" WHERE \"Key\" = @key", statements.SelectByKey);
        Assert.Equal("INSERT INTO \"TOrganization\" (\"Key\", \"Name\", \"IsActive\") VALUES (@Key, @Name, @IsActive)", statements.Insert);
        Assert.Equal("UPDATE \"TOrganization\" SET \"Name\" = @Name, \"IsActive\" = @IsActive WHERE \"Key\" = @Key", statements.Update);
        Assert.Equal("DELETE FROM \"TOrganization\" WHERE \"Key\" = @key", statements.DeleteByKey);
        Assert.Equal("SELECT \"Key\", \"Name\", \"IsActive\" FROM \"TOrganization\" WHERE \"IsActive\" = @value", statements.SelectByField(new SqlColumn("IsActive", "IsActive", isKey: false)));
    }
}
