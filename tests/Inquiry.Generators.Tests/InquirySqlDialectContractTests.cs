using Inquiry.PostgreSql;
using Inquiry.Sql;
using Inquiry.Sqlite;
using Inquiry.SqlServer;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Provider-agnostic tests for the <see cref="InquirySqlDialect"/> contract: every dialect
/// must reject the same invalid input and honor the same context invariants. Provider-specific
/// SQL formatting is verified in the per-dialect test classes.
/// </summary>
public sealed class InquirySqlDialectContractTests
{
    public static IEnumerable<object[]> AllDialects()
    {
        yield return new object[] { new SqliteInquirySqlDialect() };
        yield return new object[] { new SqlServerInquirySqlDialect() };
        yield return new object[] { new PostgreSqlInquirySqlDialect() };
    }

    private static readonly InquirySqlColumn[] _columns =
    {
        new("Key", "Key", isKey: true),
        new("Name", "Name", isKey: false),
    };

    [Theory]
    [MemberData(nameof(AllDialects))]
    public void QuoteIdentifierRejectsEmptyString(InquirySqlDialect dialect)
    {
        Assert.Throws<ArgumentException>(() => dialect.QuoteIdentifier(""));
        Assert.Throws<ArgumentException>(() => dialect.QuoteIdentifier("   "));
    }

    [Theory]
    [MemberData(nameof(AllDialects))]
    public void ParameterNameRejectsBlank(InquirySqlDialect dialect)
    {
        Assert.Throws<ArgumentException>(() => dialect.ParameterName(""));
        Assert.Throws<ArgumentException>(() => dialect.ParameterName("   "));
    }

    [Theory]
    [MemberData(nameof(AllDialects))]
    public void QuoteTableRejectsBlankTableName(InquirySqlDialect dialect)
    {
        Assert.Throws<ArgumentException>(() => dialect.QuoteTable(null, ""));
        Assert.Throws<ArgumentException>(() => dialect.QuoteTable("dbo", ""));
    }

    [Theory]
    [MemberData(nameof(AllDialects))]
    public void CreateContextRejectsNullColumns(InquirySqlDialect dialect)
    {
        Assert.Throws<ArgumentNullException>(() => dialect.CreateContext(null, "T", null!));
    }

    [Theory]
    [MemberData(nameof(AllDialects))]
    public void CreateContextRejectsEmptyColumns(InquirySqlDialect dialect)
    {
        Assert.Throws<ArgumentException>(() => dialect.CreateContext(null, "T", Array.Empty<InquirySqlColumn>()));
    }

    [Theory]
    [MemberData(nameof(AllDialects))]
    public void CreateContextRejectsMultipleKeyColumns(InquirySqlDialect dialect)
    {
        var columns = new[]
        {
            new InquirySqlColumn("A", "A", isKey: true),
            new InquirySqlColumn("B", "B", isKey: true),
        };

        Assert.Throws<ArgumentException>(() => dialect.CreateContext(null, "T", columns));
    }

    [Theory]
    [MemberData(nameof(AllDialects))]
    public void BuildersRejectNullContext(InquirySqlDialect dialect)
    {
        Assert.Throws<ArgumentNullException>(() => dialect.BuildSelectAllSql(null!));
        Assert.Throws<ArgumentNullException>(() => dialect.BuildSelectByKeySql(null!));
        Assert.Throws<ArgumentNullException>(() => dialect.BuildInsertSql(null!));
        Assert.Throws<ArgumentNullException>(() => dialect.BuildUpdateSql(null!));
        Assert.Throws<ArgumentNullException>(() => dialect.BuildDeleteByKeySql(null!));
        Assert.Throws<ArgumentNullException>(() => dialect.BuildUpsertSql(null!));
    }

    [Theory]
    [MemberData(nameof(AllDialects))]
    public void BuildSelectByFieldSqlRejectsNullColumn(InquirySqlDialect dialect)
    {
        var context = dialect.CreateContext(null, "TOrganization", _columns);
        Assert.Throws<ArgumentNullException>(() => dialect.BuildSelectByFieldSql(context, null!));
    }

    [Theory]
    [MemberData(nameof(AllDialects))]
    public void BuildInsertSqlRejectsAllGeneratedColumns(InquirySqlDialect dialect)
    {
        var context = dialect.CreateContext(
            null,
            "T",
            new[] { new InquirySqlColumn("Id", "Id", isKey: true, isGenerated: true) });

        Assert.Throws<InvalidOperationException>(() => dialect.BuildInsertSql(context));
    }

    [Theory]
    [MemberData(nameof(AllDialects))]
    public void BuildUpdateSqlRejectsEntitiesWithoutMutableColumns(InquirySqlDialect dialect)
    {
        var context = dialect.CreateContext(
            null,
            "T",
            new[] { new InquirySqlColumn("Id", "Id", isKey: true) });

        Assert.Throws<InvalidOperationException>(() => dialect.BuildUpdateSql(context));
    }

    [Theory]
    [MemberData(nameof(AllDialects))]
    public void BuildUpsertSqlRejectsGeneratedKey(InquirySqlDialect dialect)
    {
        var context = dialect.CreateContext(
            null,
            "T",
            new[]
            {
                new InquirySqlColumn("Id", "Id", isKey: true, isGenerated: true),
                new InquirySqlColumn("Name", "Name", isKey: false),
            });

        Assert.Throws<InvalidOperationException>(() => dialect.BuildUpsertSql(context));
    }

    [Theory]
    [MemberData(nameof(AllDialects))]
    public void EveryDialectExposesNonEmptyName(InquirySqlDialect dialect)
    {
        Assert.False(string.IsNullOrWhiteSpace(dialect.Name));
    }
}
