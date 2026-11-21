using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

/// <summary>
/// <c>[InquiryQuery]</c> (#620) runs handwritten SQL from a store method. Each named parameter binds
/// to the method parameter of that name, and names in literals, quoted identifiers and comments do not.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string QueryHeader = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Activity")]
        public sealed class Activity
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryColumn("TMemberKey")]
            public int MemberKey { get; set; }
        }

        [InquiryAdHoc]
        public sealed class ActivitySummaryRow
        {
            public int MemberKey { get; set; }

            public long Visits { get; set; }
        }

        public partial class ActivityStore : InquiryStore<Activity>
        {
        """;

    private static string GetActivityStore(GeneratorTestResult result)
        => Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("ActivityStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();

    private static Diagnostic[] QueryDiagnostics(GeneratorTestResult result)
        => result.RunResult.Diagnostics.Where(static d => d.Id == "INQ123").ToArray();

    [Fact]
    public void Query_ListBindsEachNamedParameterAndMapsRowsByName()
    {
        var source = QueryHeader + """
                private const string Cte = "WITH recent AS (SELECT [TMemberKey], [Id] FROM [Activity] WHERE [CheckIn] >= @startUtc) ";

                [InquiryQuery(Cte + "SELECT [TMemberKey] AS MemberKey, COUNT(*) AS Visits FROM recent WHERE [TMemberKey] = @memberKey GROUP BY [TMemberKey]")]
                public partial Task<IReadOnlyList<ActivitySummaryRow>> QueryAsync(int memberKey, DateTime startUtc, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetActivityStore(result);

        Assert.Contains("private const string _sqlQuery_QueryAsync = \"WITH recent AS (SELECT [TMemberKey], [Id] FROM [Activity] WHERE [CheckIn] >= @startUtc) SELECT [TMemberKey] AS MemberKey, COUNT(*) AS Visits FROM recent WHERE [TMemberKey] = @memberKey GROUP BY [TMemberKey]\";", text);
        Assert.Contains("_p0.ParameterName = \"@memberKey\";", text);
        Assert.Contains("_p0.DbType = global::System.Data.DbType.Int32;", text);
        Assert.Contains("_p1.ParameterName = \"@startUtc\";", text);
        Assert.Contains("return this.Inquiry.QueryListAsync<global::Demo.ActivitySummaryRow, (int Arg0, global::System.DateTime Arg1), global::Demo.ActivitySummaryRowInquiryAdHocStructMaterializer>(", text);
    }

    [Fact]
    public void Query_IgnoresNamesInLiteralsIdentifiersAndComments()
    {
        var source = QueryHeader + """
                [InquiryQuery("SELECT COUNT(*) FROM [Activity @x] WHERE [TMemberKey] = @memberKey AND Note <> 'it''s @y' AND @@ROWCOUNT >= 0 -- @z\n /* @w */")]
                public partial Task<long> CountAsync(int memberKey, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetActivityStore(result);

        Assert.Contains("_p0.ParameterName = \"@memberKey\";", text);
        Assert.DoesNotContain("_p1", text);
        Assert.Contains("return this.Inquiry.ExecuteScalarAsync<long, int>(", text);
    }

    [Fact]
    public void Query_OracleRewritesEitherPrefixToItsBindName()
    {
        var source = QueryHeader + """
                [InquiryQuery("SELECT COUNT(*) FROM Activity@remote WHERE TMemberKey = :memberKey OR TMemberKey = @memberKey OR Note = q'[:x]'")]
                public partial Task<long> CountAsync(int memberKey, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetActivityStore(result);

        var sqlLine = Assert.Single(text.Split('\n'), static line => line.Contains("_sqlQuery_CountAsync =", StringComparison.Ordinal));
        Assert.Contains("FROM Activity@remote WHERE TMemberKey = :iq1$member$", sqlLine);
        Assert.DoesNotContain(":memberKey", sqlLine);
        Assert.DoesNotContain("@memberKey", sqlLine);
        Assert.Contains("Note = q'[:x]'", sqlLine);
        Assert.Contains("_p0.ParameterName = \"iq1$member$", text);
    }

    [Fact]
    public void Query_SqliteAcceptsEachPrefixAndUsesAtSign()
    {
        var source = QueryHeader + """
                [InquiryQuery("SELECT COUNT(*) FROM Activity WHERE TMemberKey IN (@memberKey, :memberKey, $memberKey) AND `x` <> '$y'")]
                public partial Task<long> CountAsync(int memberKey, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetActivityStore(result);

        Assert.Contains("TMemberKey IN (@memberKey, @memberKey, @memberKey) AND `x` <> '$y'", text);
    }

    [Fact]
    public void Query_PostgreSqlSkipsCastsAndDollarQuotes()
    {
        var source = QueryHeader + """
                [InquiryQuery("SELECT COUNT(*)::bigint FROM \"Activity\" WHERE \"TMemberKey\" = @memberKey AND note <> $tag$ @x $tag$ AND note <> E'\\' @y'")]
                public partial Task<long> CountAsync(int memberKey, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "PostgreSql");
        AssertNoErrors(result);
        Assert.Contains("_p0.ParameterName = \"@memberKey\";", GetActivityStore(result));
    }

    [Fact]
    public void Query_MySqlSkipsBackticksHashCommentsAndBackslashEscapes()
    {
        var source = QueryHeader + """
                [InquiryQuery("SELECT COUNT(*) FROM `Activity @x` WHERE TMemberKey = @memberKey AND note <> 'it\\'s @y' # @z")]
                public partial Task<long> CountAsync(int memberKey, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "MySql");
        AssertNoErrors(result);
        Assert.Contains("_p0.ParameterName = \"@memberKey\";", GetActivityStore(result));
    }

    [Fact]
    public void Query_UnknownAndUnusedParametersReportINQ123()
    {
        var source = QueryHeader + """
                [InquiryQuery("SELECT COUNT(*) FROM [Activity] WHERE [TMemberKey] = @MemberKey")]
                public partial Task<long> CountAsync(int memberKey, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        var messages = QueryDiagnostics(result).Select(static d => d.GetMessage(CultureInfo.InvariantCulture)).ToArray();

        Assert.Equal(2, messages.Length);
        Assert.Contains(messages, static m => m.Contains("names '@MemberKey', which match no method parameter", StringComparison.Ordinal) && m.Contains("Its parameters are 'memberKey'.", StringComparison.Ordinal));
        Assert.Contains(messages, static m => m.Contains("does not use 'memberKey'", StringComparison.Ordinal));
        Assert.All(QueryDiagnostics(result), static d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    [Fact]
    public void Query_ParametersThatDifferOnlyInCaseReportINQ123()
    {
        var source = QueryHeader + """
                [InquiryQuery("SELECT COUNT(*) FROM [Activity] WHERE [TMemberKey] = @key OR [Id] = @Key")]
                public partial Task<long> CountAsync(int key, long Key, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        var diagnostic = Assert.Single(QueryDiagnostics(result));
        Assert.Contains("'key' and 'Key', which differ only in case", diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Query_ReturnShapesSelectTheMatchingCall()
    {
        var source = QueryHeader + """
                [InquiryQuery("SELECT * FROM Activity WHERE Id = @id")]
                public partial Task<Activity?> FindAsync(long id, CancellationToken cancellationToken = default);

                [InquiryQuery("DELETE FROM Activity WHERE Id = @id")]
                public partial Task<int> DeleteAsync(long id, CancellationToken cancellationToken = default);

                [InquiryQuery("SELECT COUNT(*) FROM Activity", Scalar = true)]
                public partial Task<int> CountAsync(CancellationToken cancellationToken = default);

                [InquiryQuery("SELECT MAX(TMemberKey) FROM Activity")]
                public partial Task<int?> MaxMemberAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetActivityStore(result);

        Assert.Contains("return this.Inquiry.QuerySingleOrDefaultAsync<global::Demo.Activity, long, global::Demo.ActivityInquiryEntityNameMappedMaterializer>(", text);
        Assert.Contains("return this.Inquiry.ExecuteAsync(", text);
        Assert.Contains("return this.Inquiry.ExecuteScalarAsync<int, byte>(", text);
        Assert.Contains("return this.Inquiry.ExecuteScalarAsync<int?, byte>(", text);
    }

    [Theory]
    [InlineData("public partial IAsyncEnumerable<Activity> RunAsync(CancellationToken cancellationToken = default);", "is not supported")]
    [InlineData("public partial Task<IReadOnlyList<Uri>> RunAsync(CancellationToken cancellationToken = default);", "is not an [InquiryTable]")]
    [InlineData("public partial Task<Uri> RunAsync(CancellationToken cancellationToken = default);", "neither a mapped row type nor a scalar")]
    [InlineData("public partial Task<int> RunAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);", "not a scalar type Inquiry can bind")]
    [InlineData("public partial Task<DateOnly> RunAsync(CancellationToken cancellationToken = default);", "converts on every provider")]
    [InlineData("public partial Task<Guid?> RunAsync(CancellationToken cancellationToken = default);", "converts on every provider")]
    [InlineData("public partial Task<uint> RunAsync(CancellationToken cancellationToken = default);", "converts on every provider")]
    [InlineData("public partial Task<Activity> RunAsync(CancellationToken cancellationToken = default);", "Declare Task<Activity?>")]
    [InlineData("public partial Task<long> RunAsync(CancellationToken first, CancellationToken cancellationToken = default);", "CancellationToken before the last parameter")]
    [InlineData("public partial Task<DayOfWeek> RunAsync(CancellationToken cancellationToken = default);", "converts on every provider")]
    public void Query_UnsupportedShapesReportINQ123(string declaration, string expected)
    {
        var source = QueryHeader + $$"""
                [InquiryQuery("SELECT 1")]
                {{declaration}}
            }
            """;

        var result = RunGenerator(source);
        var diagnostic = Assert.Single(QueryDiagnostics(result));
        Assert.Contains(expected, diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Query_ScalarWithRowResultReportsINQ123()
    {
        var source = QueryHeader + """
                [InquiryQuery("SELECT * FROM Activity", Scalar = true)]
                public partial Task<IReadOnlyList<Activity>> AllAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains("Scalar = true cannot be combined with a row result", Assert.Single(QueryDiagnostics(result)).GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Query_OnAViewStoreReportsINQ052()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryView("ActiveActivity")]
            public sealed class ActiveActivity
            {
                [InquiryColumn]
                public long Id { get; set; }
            }

            public partial class ActiveActivityStore : InquiryStore<ActiveActivity>
            {
                [InquiryQuery("DELETE FROM ActiveActivity")]
                public partial Task<int> ClearAsync(CancellationToken cancellationToken = default);

                [InquiryQuery("SELECT COUNT(*) FROM ActiveActivity")]
                public partial Task<long> CountAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        var messages = result.RunResult.Diagnostics.Where(static d => d.Id == "INQ052").Select(static d => d.GetMessage(CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(2, messages.Length);
        Assert.Contains(messages, static m => m.Contains("ClearAsync", StringComparison.Ordinal));
        Assert.Contains(messages, static m => m.Contains("CountAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void Query_ParameterNamesMatchingGeneratedNamesStillCompile()
    {
        var source = QueryHeader + """
                [InquiryQuery("SELECT COUNT(*) FROM Activity WHERE Id IN (@_args, @_c, @_cmd, @_p0, @Inquiry)")]
                public partial Task<long> CountAsync(long _args, long _c, long _cmd, long _p0, long Inquiry, CancellationToken cancellationToken = default);

                [InquiryQuery("SELECT * FROM Activity WHERE Id = @_args")]
                public partial Task<Activity?> FindAsync(long _args, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
    }

    [Fact]
    public void Query_ParameterNamedLikeTheSqlConstDoesNotReplaceTheSql()
    {
        var source = QueryHeader + """
                [InquiryQuery("SELECT COUNT(*) FROM Activity WHERE Note = @_sqlQuery_CountAsync")]
                public partial Task<long> CountAsync(string _sqlQuery_CountAsync, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        Assert.Contains("global::Demo.ActivityStore._sqlQuery_CountAsync,", GetActivityStore(result));
    }

    [Fact]
    public void Query_NameWithACombiningMarkMatchesItsParameter()
    {
        var source = QueryHeader + """
                [InquiryQuery("SELECT COUNT(*) FROM Activity WHERE Id = @café")]
                public partial Task<long> CountAsync(long café, CancellationToken cancellationToken = default);
            }
            """;

        AssertNoErrors(RunGenerator(source));
    }

    [Theory]
    [InlineData("SqlServer", "SELECT COUNT(*) FROM Activity WHERE Id = @id /* outer /* inner */ @ignored */")]
    [InlineData("PostgreSql", "SELECT COUNT(*) FROM Activity WHERE Id = @id /* outer /* inner */ @ignored */")]
    [InlineData("MySql", "SELECT COUNT(*) FROM Activity WHERE Id = 10--@id")]
    [InlineData("MySql", "SELECT COUNT(*) FROM Activity WHERE Id = @id -- @ignored")]
    [InlineData("Oracle", "SELECT COUNT(*) FROM \"Order Details\"@remote WHERE Id = :id")]
    [InlineData("Oracle", "SELECT COUNT(*) FROM ACTIVITY#@remote WHERE Id = :id")]
    [InlineData("SqlServer", "-- comment\\rSELECT COUNT(*) FROM Activity WHERE Id = @id")]
    [InlineData("Sqlite", "SELECT:id")]
    [InlineData("Oracle", "SELECT COUNT(*) FROM Activity WHERE Id=:id")]
    [InlineData("Oracle", "SELECT COUNT(*) FROM Activity WHERE Id = :id AND Note <> nq'[it's @ghost]'")]
    public void Query_DialectScanFindsOnlyRealParameters(string dialect, string sql)
    {
        var source = QueryHeader + $$"""
                [InquiryQuery("{{sql.Replace("\"", "\\\"", StringComparison.Ordinal)}}")]
                public partial Task<long> CountAsync(long id, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: dialect);
        AssertNoErrors(result);
    }
}
