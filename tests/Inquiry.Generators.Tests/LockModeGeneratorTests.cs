using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    private const string LockModeEntity = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TProduct")]
        public sealed class Product
        {
            [InquiryKey]
            public int Id { get; set; }

            [InquiryColumn("Name")]
            public string Name { get; set; } = string.Empty;
        }
        """;

    private static string LockModeStore(string methods) =>
        LockModeEntity + "\n\npublic partial class LockModeStore : Inquiry.Stores.InquiryStore<Demo.Product>\n{\n" + methods + "\n}\n";

    private static string GetLockModeStore(GeneratorTestResult result)
    {
        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("LockModeStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    [Fact]
    public void LockMode_SelectByKeyForUpdate_PostgreSql()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey(LockMode = InquiryLockMode.Update)]
            public partial Task<Product?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default);
            """), dialect: "PostgreSql");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("FOR UPDATE", text);
        Assert.Contains("_sqlSelectByKey_GetForUpdateAsync", text);
    }

    [Fact]
    public void LockMode_SelectAllForUpdate_PostgreSql()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectAll(LockMode = InquiryLockMode.Update)]
            public partial IAsyncEnumerable<Product> SelectForUpdateAsync(CancellationToken cancellationToken = default);
            """), dialect: "PostgreSql");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("FOR UPDATE", text);
        Assert.Contains("_sqlSelectAll_SelectForUpdateAsync", text);
    }

    [Fact]
    public void LockMode_SelectByFieldForShare_PostgreSql()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectAllByField("Name", LockMode = InquiryLockMode.Share)]
            public partial Task<IReadOnlyList<Product>> SelectByNameForShareAsync(string name, CancellationToken cancellationToken = default);
            """), dialect: "PostgreSql");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("FOR SHARE", text);
    }

    [Fact]
    public void LockMode_SelectByPredicateForUpdateNoWait_PostgreSql()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectAllByPredicate(LockMode = InquiryLockMode.UpdateNoWait)]
            [InquiryWhere("Name")]
            public partial Task<IReadOnlyList<Product>> SearchForUpdateAsync(string name, CancellationToken cancellationToken = default);
            """), dialect: "PostgreSql");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("FOR UPDATE NOWAIT", text);
    }

    [Fact]
    public void LockMode_UpdateSkipLocked_PostgreSql()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey(LockMode = InquiryLockMode.UpdateSkipLocked)]
            public partial Task<Product?> GetSkipLockedAsync(int id, CancellationToken cancellationToken = default);
            """), dialect: "PostgreSql");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("FOR UPDATE SKIP LOCKED", text);
    }

    [Fact]
    public void LockMode_SelectByKeyForUpdate_SqlServer()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey(LockMode = InquiryLockMode.Update)]
            public partial Task<Product?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default);
            """), dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("WITH (UPDLOCK, ROWLOCK)", text);
        Assert.DoesNotContain("FOR UPDATE", text);
    }

    [Fact]
    public void LockMode_Share_SqlServer()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey(LockMode = InquiryLockMode.Share)]
            public partial Task<Product?> GetForShareAsync(int id, CancellationToken cancellationToken = default);
            """), dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("WITH (HOLDLOCK, ROWLOCK)", text);
    }

    [Fact]
    public void LockMode_UpdateNoWait_SqlServer()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey(LockMode = InquiryLockMode.UpdateNoWait)]
            public partial Task<Product?> GetNoWaitAsync(int id, CancellationToken cancellationToken = default);
            """), dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("WITH (UPDLOCK, ROWLOCK, NOWAIT)", text);
    }

    [Fact]
    public void LockMode_UpdateSkipLocked_SqlServer()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey(LockMode = InquiryLockMode.UpdateSkipLocked)]
            public partial Task<Product?> GetSkipLockedAsync(int id, CancellationToken cancellationToken = default);
            """), dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("WITH (UPDLOCK, ROWLOCK, READPAST)", text);
    }

    [Fact]
    public void LockMode_Sqlite_ReportsINQ039()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey(LockMode = InquiryLockMode.Update)]
            public partial Task<Product?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default);
            """));
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ039");
    }

    [Fact]
    public void LockMode_ForUpdate_MySql()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey(LockMode = InquiryLockMode.Update)]
            public partial Task<Product?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default);
            """), dialect: "MySql");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("FOR UPDATE", text);
    }

    [Fact]
    public void LockMode_ForShare_MariaDb_EmitsLockInShareMode()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey(LockMode = InquiryLockMode.Share)]
            public partial Task<Product?> GetForShareAsync(int id, CancellationToken cancellationToken = default);
            """), dialect: "MariaDb");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("LOCK IN SHARE MODE", text);
        Assert.DoesNotContain("FOR SHARE", text);
    }

    [Fact]
    public void LockMode_ForUpdate_Oracle()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey(LockMode = InquiryLockMode.Update)]
            public partial Task<Product?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default);
            """), dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("FOR UPDATE", text);
    }

    [Fact]
    public void LockMode_ForShare_Oracle_ReportsINQ039()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey(LockMode = InquiryLockMode.Share)]
            public partial Task<Product?> GetForShareAsync(int id, CancellationToken cancellationToken = default);
            """), dialect: "Oracle");
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ039");
    }

    [Fact]
    public void LockMode_None_DoesNotEmitLockClause()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey]
            public partial Task<Product?> GetAsync(int id, CancellationToken cancellationToken = default);
            """), dialect: "PostgreSql");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.DoesNotContain("FOR UPDATE", text);
        Assert.DoesNotContain("FOR SHARE", text);
    }

    [Fact]
    public void LockMode_SqlServerTableHintInsertedAfterTable()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey(LockMode = InquiryLockMode.Update)]
            public partial Task<Product?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default);
            """), dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("[TProduct] WITH (UPDLOCK, ROWLOCK) WHERE", text);
    }

    private const string LockShapeEntity = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Paging;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Job")]
        public sealed class Job
        {
            [InquiryKey]
            public int Id { get; set; }

            [InquiryColumn("State")]
            public int State { get; set; }

            [InquiryColumn("Note")]
            public string Note { get; set; } = string.Empty;
        }

        [InquiryProjection(typeof(Job))]
        public sealed record JobSummary
        {
            [InquiryColumn("Id")]
            public int Id { get; init; }

            [InquiryColumn("State")]
            public int State { get; init; }
        }
        """;

    private static string LockShapeStore(string methods) =>
        LockShapeEntity + "\n\npublic partial class JobStore : Inquiry.Stores.InquiryStore<Demo.Job>\n{\n" + methods + "\n}\n";

    private static string StatusStore(string methods, string table = "[InquiryTable(\"Status\")]") => """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Paging;
        using Inquiry.Stores;

        namespace Demo;

        """ + table + """

        public sealed class StatusRow
        {
            [InquiryKey]
            public int Id { get; set; }

            [InquiryColumn("Status")]
            public int Code { get; set; }
        }

        [InquiryProjection(typeof(StatusRow))]
        public sealed record StatusCode
        {
            [InquiryColumn("Status")]
            public int Code { get; init; }
        }

        public partial class StatusStore : Inquiry.Stores.InquiryStore<Demo.StatusRow>
        {
        """ + methods + "\n}\n";

    private static string GetGeneratedStore(GeneratorTestResult result, string store)
    {
        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            t => t.FilePath.EndsWith(store + ".InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    // Returns the SQL text of one generated const, unescaped, so a test can compare the whole statement.
    private static string GeneratedSql(string storeText, string field)
    {
        var prefix = "private const string " + field + " = \"";
        foreach (var raw in storeText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith(prefix, StringComparison.Ordinal) && line.EndsWith("\";", StringComparison.Ordinal))
            {
                return line.Substring(prefix.Length, line.Length - prefix.Length - 2).Replace("\\\"", "\"");
            }
        }

        Assert.Fail("The generated store has no const " + field + ".");
        return string.Empty;
    }

    private static void AssertSingleLockShapeError(GeneratorTestResult result, string source, string method, string dialect, string reason)
    {
        var diagnostic = Assert.Single(result.RunResult.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("INQ039", diagnostic.Id);
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("'" + method + "'", message);
        Assert.Contains("'" + dialect + "'", message);
        Assert.Contains(reason, message);
        Assert.Equal(method, source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
    }

    [Theory]
    [InlineData("PostgreSql", "Update", "SELECT \"Id\", \"State\" FROM \"Job\" FOR UPDATE")]
    [InlineData("PostgreSql", "UpdateNoWait", "SELECT \"Id\", \"State\" FROM \"Job\" FOR UPDATE NOWAIT")]
    [InlineData("PostgreSql", "UpdateSkipLocked", "SELECT \"Id\", \"State\" FROM \"Job\" FOR UPDATE SKIP LOCKED")]
    [InlineData("PostgreSql", "Share", "SELECT \"Id\", \"State\" FROM \"Job\" FOR SHARE")]
    [InlineData("MySql", "Update", "SELECT `Id`, `State` FROM `Job` FOR UPDATE")]
    [InlineData("MySql", "Share", "SELECT `Id`, `State` FROM `Job` FOR SHARE")]
    [InlineData("MariaDb", "Update", "SELECT `Id`, `State` FROM `Job` FOR UPDATE")]
    [InlineData("MariaDb", "Share", "SELECT `Id`, `State` FROM `Job` LOCK IN SHARE MODE")]
    [InlineData("SqlServer", "Update", "SELECT [Id], [State] FROM [Job] WITH (UPDLOCK, ROWLOCK)")]
    [InlineData("SqlServer", "UpdateNoWait", "SELECT [Id], [State] FROM [Job] WITH (UPDLOCK, ROWLOCK, NOWAIT)")]
    [InlineData("SqlServer", "UpdateSkipLocked", "SELECT [Id], [State] FROM [Job] WITH (UPDLOCK, ROWLOCK, READPAST)")]
    [InlineData("SqlServer", "Share", "SELECT [Id], [State] FROM [Job] WITH (HOLDLOCK, ROWLOCK)")]
    [InlineData("Oracle", "Update", "SELECT Id, State FROM Job FOR UPDATE")]
    [InlineData("Oracle", "UpdateSkipLocked", "SELECT Id, State FROM Job FOR UPDATE SKIP LOCKED")]
    public void LockMode_ProjectionSelectAll_LocksTheProjectionQuery(string dialect, string mode, string expected)
    {
        var result = RunGenerator(LockShapeStore($$"""
            [InquirySelectAll(LockMode = InquiryLockMode.{{mode}})]
            public partial Task<IReadOnlyList<JobSummary>> SummariesAsync(CancellationToken cancellationToken = default);
            """), dialect: dialect);
        AssertNoErrors(result);
        var text = GetGeneratedStore(result, "JobStore");

        Assert.Equal(expected, GeneratedSql(text, "_sqlProj_SummariesAsync"));
        // Declared once and executed by the method.
        Assert.Equal(2, text.Split("_sqlProj_SummariesAsync").Length - 1);
        Assert.DoesNotContain("_sqlSelectAll_SummariesAsync", text);
    }

    [Theory]
    [InlineData("PostgreSql", "SELECT \"Id\", \"State\" FROM \"Job\" WHERE \"State\" = @State FOR UPDATE SKIP LOCKED")]
    [InlineData("MySql", "SELECT `Id`, `State` FROM `Job` WHERE `State` = @State FOR UPDATE SKIP LOCKED")]
    [InlineData("MariaDb", "SELECT `Id`, `State` FROM `Job` WHERE `State` = @State FOR UPDATE SKIP LOCKED")]
    [InlineData("SqlServer", "SELECT [Id], [State] FROM [Job] WITH (UPDLOCK, ROWLOCK, READPAST) WHERE [State] = @State")]
    [InlineData("Oracle", "SELECT Id, State FROM Job WHERE State = :iq1$Statex$e22a866a45ba52 FOR UPDATE SKIP LOCKED")]
    public void LockMode_ProjectionSelectAllByField_LocksTheProjectionQuery(string dialect, string expected)
    {
        var result = RunGenerator(LockShapeStore("""
            [InquirySelectAllByField("State", LockMode = InquiryLockMode.UpdateSkipLocked)]
            public partial IAsyncEnumerable<JobSummary> SummariesByStateAsync(int state, CancellationToken cancellationToken = default);
            """), dialect: dialect);
        AssertNoErrors(result);
        var text = GetGeneratedStore(result, "JobStore");

        Assert.Equal(expected, GeneratedSql(text, "_sqlProj_SummariesByStateAsync"));
        // Declared once and executed by the method.
        Assert.Equal(2, text.Split("_sqlProj_SummariesByStateAsync").Length - 1);
        Assert.DoesNotContain("_sqlSelectBy_State_SummariesByStateAsync", text);
    }

    [Fact]
    public void LockMode_OrderedProjection_StillLocksThePlanQuery()
    {
        var result = RunGenerator(LockShapeStore("""
            [InquirySelectAll(OrderBy = "Id ASC", LockMode = InquiryLockMode.Update)]
            public partial Task<IReadOnlyList<JobSummary>> OrderedSummariesAsync(CancellationToken cancellationToken = default);
            """), dialect: "PostgreSql");
        AssertNoErrors(result);
        var text = GetGeneratedStore(result, "JobStore");

        Assert.Equal("SELECT \"Id\", \"State\" FROM \"Job\" ORDER BY \"Id\" ASC FOR UPDATE", GeneratedSql(text, "_sql_OrderedSummariesAsync"));
        Assert.DoesNotContain("_sqlSelectAll_OrderedSummariesAsync", text);
    }

    [Fact]
    public void LockMode_ProjectionOnSqlite_ReportsINQ039()
    {
        var source = LockShapeStore("""
            [InquirySelectAll(LockMode = InquiryLockMode.Update)]
            public partial Task<IReadOnlyList<JobSummary>> SummariesAsync(CancellationToken cancellationToken = default);
            """);
        var result = RunGenerator(source);

        AssertSingleLockShapeError(result, source, "SummariesAsync", "Sqlite", "SQLite does not support row-level locking.");
    }

    [Theory]
    [InlineData("PostgreSql", "[InquirySelectAll(Distinct = true, LockMode = InquiryLockMode.Update)]", "Task<IReadOnlyList<Job>> M(CancellationToken cancellationToken = default)", "PostgreSQL does not support row locking with DISTINCT.")]
    [InlineData("PostgreSql", "[InquirySelectAll(Distinct = true, LockMode = InquiryLockMode.Share)]", "Task<IReadOnlyList<JobSummary>> M(CancellationToken cancellationToken = default)", "PostgreSQL does not support row locking with DISTINCT.")]
    [InlineData("PostgreSql", "[InquirySelectAllByField(\"State\", Distinct = true, LockMode = InquiryLockMode.UpdateSkipLocked)]", "Task<IReadOnlyList<Job>> M(int state, CancellationToken cancellationToken = default)", "PostgreSQL does not support row locking with DISTINCT.")]
    [InlineData("PostgreSql", "[InquirySelectAllByPredicate(Distinct = true, LockMode = InquiryLockMode.Share)][InquiryWhere(\"State\")]", "Task<IReadOnlyList<Job>> M(int state, CancellationToken cancellationToken = default)", "PostgreSQL does not support row locking with DISTINCT.")]
    [InlineData("PostgreSql", "[InquirySelectAll(OrderBy = \"Id ASC\", Paged = true, Distinct = true, LockMode = InquiryLockMode.Update)]", "Task<IReadOnlyList<Job>> M(int offset, int limit, CancellationToken cancellationToken = default)", "PostgreSQL does not support row locking with DISTINCT.")]
    [InlineData("Oracle", "[InquirySelectAll(Distinct = true, LockMode = InquiryLockMode.Update)]", "Task<IReadOnlyList<Job>> M(CancellationToken cancellationToken = default)", "Oracle does not support row locking with DISTINCT.")]
    [InlineData("Oracle", "[InquirySelectAllByField(\"State\", Distinct = true, LockMode = InquiryLockMode.UpdateNoWait)]", "Task<IReadOnlyList<JobSummary>> M(int state, CancellationToken cancellationToken = default)", "Oracle does not support row locking with DISTINCT.")]
    [InlineData("Oracle", "[InquirySelectAll(OrderBy = \"Id ASC\", Paged = true, LockMode = InquiryLockMode.Update)]", "Task<IReadOnlyList<Job>> M(int offset, int limit, CancellationToken cancellationToken = default)", "Oracle does not support row locking with OFFSET/FETCH paging.")]
    [InlineData("Oracle", "[InquirySelectAllByField(\"State\", OrderBy = \"Id ASC\", Paged = true, LockMode = InquiryLockMode.UpdateSkipLocked)]", "Task<IReadOnlyList<JobSummary>> M(int state, int offset, int limit, CancellationToken cancellationToken = default)", "Oracle does not support row locking with OFFSET/FETCH paging.")]
    [InlineData("Oracle", "[InquirySelectAllByPredicate(OrderBy = \"Id ASC\", LockMode = InquiryLockMode.Update)][InquiryWhere(\"State\")]", "Task<InquiryPagedResult<Job>> M(int state, int offset, int limit, CancellationToken cancellationToken = default)", "Oracle does not support row locking with OFFSET/FETCH paging.")]
    public void LockMode_ShapeTheProviderRejects_ReportsINQ039AtTheMethod(string dialect, string attributes, string signature, string reason)
    {
        var source = LockShapeStore(attributes + "\npublic partial " + signature + ";\n\n[InquirySelectOneByKey(LockMode = InquiryLockMode.Update)]\npublic partial Task<Job?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default);");
        var result = RunGenerator(source, dialect: dialect);

        AssertSingleLockShapeError(result, source, "M", dialect, reason);
        var text = GetGeneratedStore(result, "JobStore");
        Assert.DoesNotContain("_M = ", text);
        Assert.Contains("_sqlSelectByKey_GetForUpdateAsync", text);
    }

    [Fact]
    public void LockMode_ShapeTheProviderRejects_LoweredINQ039EmitsAThrowingStub()
    {
        var result = RunGenerator(LockShapeStore("""
            [InquirySelectAll(Distinct = true, LockMode = InquiryLockMode.Update)]
            public partial Task<IReadOnlyList<Job>> DistinctForUpdateAsync(CancellationToken cancellationToken = default);
            """), dialect: "Oracle", unsupportedOperationSeverity: ReportDiagnostic.Warn);

        var diagnostic = Assert.Single(result.RunResult.Diagnostics, static d => d.Id == "INQ039");
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        var text = GetGeneratedStore(result, "JobStore");
        Assert.Contains("throw new global::System.NotSupportedException(", text);
        Assert.DoesNotContain("FOR UPDATE", text);
    }

    [Theory]
    [InlineData("MySql", "Distinct = true", "SELECT DISTINCT `Id`, `State` FROM `Job` FOR UPDATE")]
    [InlineData("MariaDb", "Distinct = true", "SELECT DISTINCT `Id`, `State` FROM `Job` FOR UPDATE")]
    [InlineData("SqlServer", "Distinct = true", "SELECT DISTINCT [Id], [State] FROM [Job] WITH (UPDLOCK, ROWLOCK)")]
    [InlineData("PostgreSql", "OrderBy = \"Id ASC\", Paged = true", "SELECT \"Id\", \"State\" FROM \"Job\" ORDER BY \"Id\" ASC LIMIT @__limit OFFSET @__offset FOR UPDATE")]
    [InlineData("MySql", "OrderBy = \"Id ASC\", Paged = true", "SELECT `Id`, `State` FROM `Job` ORDER BY `Id` ASC LIMIT @__limit OFFSET @__offset FOR UPDATE")]
    [InlineData("MariaDb", "OrderBy = \"Id ASC\", Paged = true", "SELECT `Id`, `State` FROM `Job` ORDER BY `Id` ASC LIMIT @__limit OFFSET @__offset FOR UPDATE")]
    [InlineData("SqlServer", "OrderBy = \"Id ASC\", Paged = true", "SELECT [Id], [State] FROM [Job] WITH (UPDLOCK, ROWLOCK) ORDER BY [Id] ASC OFFSET @__offset ROWS FETCH NEXT @__limit ROWS ONLY")]
    [InlineData("MySql", "OrderBy = \"Id ASC\", Paged = true, Distinct = true", "SELECT DISTINCT `Id`, `State` FROM `Job` ORDER BY `Id` ASC LIMIT @__limit OFFSET @__offset FOR UPDATE")]
    [InlineData("SqlServer", "OrderBy = \"Id ASC\", Paged = true, Distinct = true", "SELECT DISTINCT [Id], [State] FROM [Job] WITH (UPDLOCK, ROWLOCK) ORDER BY [Id] ASC OFFSET @__offset ROWS FETCH NEXT @__limit ROWS ONLY")]
    public void LockMode_ShapeTheProviderAccepts_EmitsTheLockedQuery(string dialect, string options, string expected)
    {
        var paged = options.Contains("Paged", StringComparison.Ordinal);
        var result = RunGenerator(LockShapeStore($$"""
            [InquirySelectAll({{options}}, LockMode = InquiryLockMode.Update)]
            public partial Task<IReadOnlyList<JobSummary>> M({{(paged ? "int offset, int limit, " : string.Empty)}}CancellationToken cancellationToken = default);
            """), dialect: dialect);
        AssertNoErrors(result);
        var text = GetGeneratedStore(result, "JobStore");

        Assert.Equal(expected, GeneratedSql(text, paged ? "_sql_M" : "_sqlProj_M"));
    }

    [Theory]
    [InlineData("Update", "WITH (UPDLOCK, ROWLOCK)")]
    [InlineData("UpdateNoWait", "WITH (UPDLOCK, ROWLOCK, NOWAIT)")]
    [InlineData("UpdateSkipLocked", "WITH (UPDLOCK, ROWLOCK, READPAST)")]
    [InlineData("Share", "WITH (HOLDLOCK, ROWLOCK)")]
    public void LockMode_SqlServerColumnNamedLikeItsTable_PlacesTheHintAfterTheFromTable(string mode, string hint)
    {
        var result = RunGenerator(StatusStore($$"""
                [InquirySelectOneByKey(LockMode = InquiryLockMode.{{mode}})]
                public partial Task<StatusRow?> GetLockedAsync(int id, CancellationToken cancellationToken = default);

                [InquirySelectAll(LockMode = InquiryLockMode.{{mode}})]
                public partial Task<IReadOnlyList<StatusRow>> AllLockedAsync(CancellationToken cancellationToken = default);

                [InquirySelectAllByField("Code", LockMode = InquiryLockMode.{{mode}})]
                public partial Task<IReadOnlyList<StatusRow>> ByCodeLockedAsync(int code, CancellationToken cancellationToken = default);

                [InquirySelectAllByPredicate(LockMode = InquiryLockMode.{{mode}})]
                [InquiryWhere("Code")]
                public partial Task<IReadOnlyList<StatusRow>> WhereCodeLockedAsync(int code, CancellationToken cancellationToken = default);
            """), dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetGeneratedStore(result, "StatusStore");

        Assert.Equal("SELECT [Id], [Status] FROM [Status] " + hint + " WHERE [Id] = @Id", GeneratedSql(text, "_sqlSelectByKey_GetLockedAsync"));
        Assert.Equal("SELECT [Id], [Status] FROM [Status] " + hint, GeneratedSql(text, "_sqlSelectAll_AllLockedAsync"));
        Assert.Equal("SELECT [Id], [Status] FROM [Status] " + hint + " WHERE [Status] = @Code", GeneratedSql(text, "_sqlSelectBy_Code_ByCodeLockedAsync"));
        Assert.Equal("SELECT [Id], [Status] FROM [Status] " + hint + " WHERE [Status] = @Code", GeneratedSql(text, "_sqlPredicate_WhereCodeLockedAsync"));
    }

    [Fact]
    public void LockMode_SqlServerSchemaQualifiedTableAndProjection_PlaceTheHintAfterTheFromTable()
    {
        var result = RunGenerator(StatusStore("""
                [InquirySelectOneByKey(LockMode = InquiryLockMode.Update)]
                public partial Task<StatusRow?> GetLockedAsync(int id, CancellationToken cancellationToken = default);

                [InquirySelectAllByField("Code", LockMode = InquiryLockMode.UpdateSkipLocked)]
                public partial Task<IReadOnlyList<StatusCode>> CodesLockedAsync(int code, CancellationToken cancellationToken = default);
            """, table: "[InquiryTable(\"Status\", Schema = \"ops\")]"), dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetGeneratedStore(result, "StatusStore");

        Assert.Equal("SELECT [Id], [Status] FROM [ops].[Status] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = @Id", GeneratedSql(text, "_sqlSelectByKey_GetLockedAsync"));
        Assert.Equal("SELECT [Status] FROM [ops].[Status] WITH (UPDLOCK, ROWLOCK, READPAST) WHERE [Status] = @Code", GeneratedSql(text, "_sqlProj_CodesLockedAsync"));
    }

    [Fact]
    public void LockMode_SqlServerPagedResult_LocksTheRowsButNotTheCount()
    {
        var result = RunGenerator(StatusStore("""
                [InquirySelectAll(OrderBy = "Id ASC", LockMode = InquiryLockMode.Update)]
                public partial Task<InquiryPagedResult<StatusRow>> PageLockedAsync(int offset, int limit, CancellationToken cancellationToken = default);
            """), dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetGeneratedStore(result, "StatusStore");

        Assert.Equal(
            "SELECT [Id], [Status] FROM [Status] WITH (UPDLOCK, ROWLOCK) ORDER BY [Id] ASC OFFSET @__offset ROWS FETCH NEXT @__limit ROWS ONLY",
            GeneratedSql(text, "_sql_PageLockedAsync"));
        Assert.Equal("SELECT COUNT_BIG(*) FROM [Status]", GeneratedSql(text, "_sqlCount_PageLockedAsync"));
    }

    [Fact]
    public void LockMode_SqlServerApplyLockClause_RejectsSqlWithoutTheStructuralHint()
    {
        var builderType = typeof(global::Inquiry.SqlServer.Analyzer.InquirySqlServerGenerator).Assembly
            .GetType("Inquiry.SqlServer.Analyzer.SqlServerSqlBuilder", throwOnError: true)!;
        var builder = (Inquiry.Generators.Abstractions.SqlBuilder)Activator.CreateInstance(builderType, nonPublic: true)!;
        var unlocked = new Inquiry.Generators.Abstractions.SqlBuildContext(builder, null, "Status", Array.Empty<Inquiry.Generators.Abstractions.IColumn>());
        var locked = new Inquiry.Generators.Abstractions.SqlBuildContext(builder, null, "Status", Array.Empty<Inquiry.Generators.Abstractions.IColumn>()) { LockMode = 1 };

        Assert.Equal("SELECT 1", builder.ApplyLockClause("SELECT 1", unlocked, 0));
        Assert.Throws<NotSupportedException>(() => builder.ApplyLockClause("SELECT 1", unlocked, 1));
        Assert.Throws<NotSupportedException>(() => builder.ApplyLockClause("SELECT 1", locked, 1));
        Assert.Throws<NotSupportedException>(() => builder.ApplyLockClause("SELECT [Status] FROM [Status]", locked, 1));
        Assert.Equal(
            "SELECT [Status] FROM [Status] WITH (UPDLOCK, ROWLOCK)",
            builder.ApplyLockClause("SELECT [Status] FROM [Status] WITH (UPDLOCK, ROWLOCK)", locked, 1));
    }
}
