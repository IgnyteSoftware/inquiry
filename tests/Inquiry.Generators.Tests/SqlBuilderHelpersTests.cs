using System.Collections.Generic;
using Inquiry.Generators.Abstractions;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Foundation: the shared <c>AppendWhere</c> primitive that every WHERE-shaping
/// workstream (richer predicates, soft deletes, optimistic concurrency, keyset pagination) composes
/// through, so AND-joining is implemented once rather than per provider.
/// </summary>
public class SqlBuilderHelpersTests
{
    private sealed class TestSqlBuilder : SqlBuilder
    {
        public override string DialectName => "Test";
        public override string ProviderId => "test";
        public override string QuoteIdentifier(string identifier) => "\"" + identifier + "\"";
        public override string BuildSelectByKeySql(SqlBuildContext context) => string.Empty;
        public override string BuildInsertSql(SqlBuildContext context) => string.Empty;
        public override string BuildInsertReturningSql(SqlBuildContext context) => string.Empty;
        public override string BuildUpdateSql(SqlBuildContext context) => string.Empty;
        public override string BuildUpdateReturningSql(SqlBuildContext context) => string.Empty;
        public override string BuildDeleteByKeySql(SqlBuildContext context) => string.Empty;
        public override string BuildUpsertSql(SqlBuildContext context) => string.Empty;
        public override string BuildUpsertReturningSql(SqlBuildContext context) => string.Empty;
        protected override string MapColumnType(IColumn column) => "TEXT";
        protected override string GeneratedKeyClause(IColumn column) => "INTEGER PRIMARY KEY";

        public static string Combine(string where, string? extra) => AppendWhere(where, extra);
    }

    [Fact]
    public void AppendWhere_NullExtra_ReturnsExistingUnchanged()
        => Assert.Equal("a = @a", TestSqlBuilder.Combine("a = @a", null));

    [Fact]
    public void AppendWhere_EmptyExtra_ReturnsExistingUnchanged()
        => Assert.Equal("a = @a", TestSqlBuilder.Combine("a = @a", ""));

    [Fact]
    public void AppendWhere_BothPresent_AndCombines()
        => Assert.Equal("a = @a AND b = 0", TestSqlBuilder.Combine("a = @a", "b = 0"));

    [Fact]
    public void AppendWhere_EmptyExisting_ReturnsExtraAlone()
        => Assert.Equal("b = 0", TestSqlBuilder.Combine("", "b = 0"));
}
