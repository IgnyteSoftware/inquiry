using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Inquiry.Generators.Tests;

/// <summary>
/// INQ120 (<see cref="InquiryResultColumnAnalyzer"/>): constant handwritten SQL whose result columns cannot
/// match the generated result shape of the target type warns at the call site. SQL whose result list cannot
/// be known stays silent. The SQL shapes come from a SQL Server application upgrade (#605).
/// </summary>
public sealed class ResultColumnAnalyzerTests
{
    private const string Types = """
        [InquiryAdHoc]
        public sealed class CheckInCounts
        {
            public int CheckIns { get; set; }
            public int CheckOuts { get; set; }
        }

        [InquiryAdHoc]
        public sealed class CodeClaim
        {
            public Guid MemberKey { get; set; }
            public string Outcome { get; set; } = "";
            public DateTime ExpiresUtc { get; set; }
        }

        [InquiryAdHoc]
        public sealed class OpenCheckIn
        {
            public DateTime? OpenCheckInToday { get; set; }
        }

        [InquiryAdHoc]
        public sealed class PublishedDocument
        {
            public string Name { get; set; } = "";
            public DateTime PublishedDateTimeUtc { get; set; }
            public int ConsentCount { get; set; }
        }

        [InquiryAdHoc]
        public sealed class MemberPreference
        {
            public Guid MemberKey { get; set; }
            public string Email { get; set; } = "";
            public bool HasOverride { get; set; }
            public bool OptedOut { get; set; }
            public string PreferenceEmail { get; set; } = "";
        }

        [InquiryAdHoc]
        public sealed class CodeSend
        {
            public string Email { get; set; } = "";
            public string CodeDelivery { get; set; } = "";
            public Guid CodeSendId { get; set; }
        }

        [InquiryAdHoc]
        public sealed class Total
        {
            public int Count { get; set; }
        }

        [InquiryTable("Members")]
        public sealed class Member
        {
            [InquiryKey("member_key")]
            public Guid MemberKey { get; set; }

            [InquiryColumn("email_address")]
            public string Email { get; set; } = "";
        }

        [InquiryAdHoc]
        public sealed class Flag
        {
            public bool IsNull { get; set; }
        }

        [InquiryAdHoc]
        public sealed class Login
        {
            public string User { get; set; } = "";
        }

        public sealed class Plain
        {
            public int Id { get; set; }
        }
        """;

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string members, string? dialect = "SqlServer")
    {
        var dialectAttribute = dialect is null ? string.Empty : $"[assembly: InquiryDialect(\"{dialect}\")]";
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Data;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Commands;
            using Inquiry.Entities;
            using Inquiry.Transactions;

            {{dialectAttribute}}

            {{Types}}

            public class Demo
            {
                private const string CountsCte = "WITH c AS (SELECT 1 AS n) ";

                private readonly IInquiry _inquiry;
                private readonly IInquiryTransaction _transaction;

                public Demo(IInquiry inquiry, IInquiryTransaction transaction)
                {
                    _inquiry = inquiry;
                    _transaction = transaction;
                }

                {{members}}
            }
            """;

        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(global::Inquiry.IInquiry).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "ResultColumnAnalyzerTests",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        Assert.Empty(compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));

        var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new InquiryResultColumnAnalyzer()));
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static string Message(Diagnostic diagnostic)
        => diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task UnaliasedScalarSubqueriesReportEachExpectedProperty()
    {
        var diagnostics = await AnalyzeAsync(""""
            public Task<CheckInCounts?> Run(Guid site) => _inquiry.QuerySingleOrDefaultAsync<CheckInCounts>(
                $"""
                SELECT (SELECT COUNT(*) FROM [CheckIns] WHERE [SiteKey] = {site}),
                       (SELECT COUNT(*) FROM [CheckOuts] WHERE [SiteKey] = {site})
                """);
            """");

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, static d =>
        {
            Assert.Equal("INQ120", d.Id);
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        });
        Assert.Contains("property 'CheckIns'", Message(diagnostics[0]), StringComparison.Ordinal);
        Assert.Contains("property 'CheckOuts'", Message(diagnostics[1]), StringComparison.Ordinal);
        Assert.Contains("(unnamed) (SELECT COUNT(*) FROM [CheckIns]", Message(diagnostics[0]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenamedColumnReportsTheMissingNameAndListsTheReturnedNames()
    {
        var diagnostics = await AnalyzeAsync("""
            public Task<IReadOnlyList<CodeClaim>> Run(Guid code) => _inquiry.QueryListAsync<CodeClaim>(
                $"SELECT [TMemberKey], [Outcome], [ExpiresUtc] FROM [Claims] WHERE [CodeKey] = {code}");
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(
            "The SQL read into 'CodeClaim' has no column named 'MemberKey' for property 'MemberKey'. The SQL returns: TMemberKey, Outcome, ExpiresUtc. Give each result column the name of the column its property maps to, for example with an alias.",
            Message(diagnostic));
    }

    [Fact]
    public async Task UnaliasedOutputExpressionsReportAndAliasedOnesDoNot()
    {
        const string update = "UPDATE TOP (1) a SET a.[SentUtc] = SYSUTCDATETIME(), a.[Attempts] = a.[Attempts] + 1 OUTPUT {0}, {1}, deleted.[CodeSendId] FROM [CodeSends] a WHERE a.[MemberKey] = {{member}}";
        const string delivery = "CASE WHEN deleted.[PhoneNumber] IS NULL THEN 'Email' ELSE 'Sms' END";
        const string destination = "COALESCE(deleted.[PhoneNumber], deleted.[Email])";

        var reported = await AnalyzeAsync(
            "public Task<CodeSend?> Run(IInquiryTransaction tx, Guid member) => tx.QuerySingleOrDefaultAsync<CodeSend>($\"" +
            string.Format(System.Globalization.CultureInfo.InvariantCulture, update, destination, delivery) + "\");");
        Assert.Equal(["CodeDelivery", "Email"], Properties(reported));

        var silent = await AnalyzeAsync(
            "public Task<CodeSend?> Run(IInquiryTransaction tx, Guid member) => tx.QuerySingleOrDefaultAsync<CodeSend>($\"" +
            string.Format(System.Globalization.CultureInfo.InvariantCulture, update, destination + " AS [Email]", delivery + " AS [CodeDelivery]") + "\");");
        Assert.Empty(silent);
    }

    [Fact]
    public async Task ApplicationShapesThroughHelpersAndTransactionsReport()
    {
        var diagnostics = await AnalyzeAsync(""""
            private static FormattableString OpenSql(Guid member) => $"""
                SELECT (SELECT MAX([CheckInDateTime]) FROM [CheckIns] WHERE [MemberKey] = {member})
                """;

            public Task<OpenCheckIn?> Open(Guid member) => _inquiry.QuerySingleOrDefaultAsync<OpenCheckIn>(OpenSql(member));

            public Task<IReadOnlyList<PublishedDocument>> Documents() => _transaction.QueryListAsync<PublishedDocument>(
                $"""
                SELECT d.[Name], d.[DateCreated], (SELECT COUNT(*) FROM [Consents] c WHERE c.[DocumentKey] = d.[Key])
                FROM [Documents] d
                """);

            public Task<IReadOnlyList<MemberPreference>> Preferences(Guid org) => _inquiry.QueryListAsync<MemberPreference>(
                FormattableStringFactory.Create(CountsCte + """
                    SELECT m.[Key], m.[Email],
                           CAST(CASE WHEN p.[MemberKey] IS NULL THEN 0 ELSE 1 END AS BIT),
                           COALESCE(p.[OptedOut], CAST(0 AS BIT)),
                           COALESCE(p.[Email], m.[Email])
                    FROM [Members] m LEFT JOIN [Preferences] p ON p.[MemberKey] = m.[Key]
                    WHERE m.[OrganizationKey] = {0}
                    """, org));
            """");

        Assert.Equal(
            ["ConsentCount", "HasOverride", "MemberKey", "OpenCheckInToday", "OptedOut", "PreferenceEmail", "PublishedDateTimeUtc"],
            Properties(diagnostics));
    }

    [Fact]
    public async Task CorrectSqlInSupportedFormsIsSilent()
    {
        var diagnostics = await AnalyzeAsync(""""
            private const string MemberCte = """
                WITH active ([Key], [Email]) AS (SELECT [Key], [Email] FROM [Members] WHERE [Active] = 1),
                     recent AS (SELECT [MemberKey] FROM [Logins] WHERE [Utc] > {0})
                """;

            public Task<IReadOnlyList<CodeClaim>> Cte(DateTime since) => _inquiry.QueryListAsync<CodeClaim>(
                FormattableStringFactory.Create(MemberCte + """
                    SELECT a.[Key] AS [MemberKey], 'ok' AS [Outcome], SYSUTCDATETIME() AS ExpiresUtc
                    FROM active a JOIN recent r ON r.[MemberKey] = a.[Key]
                    """, since));

            public Task<IReadOnlyList<CheckInCounts>> Union() => _inquiry.QueryListAsync<CheckInCounts>(
                $"""
                SELECT COUNT(*) AS [CheckIns], 0 AS [CheckOuts] FROM [CheckIns]
                UNION ALL
                SELECT 0, COUNT(*) FROM [CheckOuts]
                """);

            public Task<CheckInCounts?> TSqlAliases() => _inquiry.QuerySingleOrDefaultAsync<CheckInCounts>(
                $"SELECT CheckIns = (SELECT COUNT(*) FROM [CheckIns]), COUNT(*) CheckOuts, COUNT(*) OVER () AS [Ignored] FROM [CheckOuts]");

            public Task<IReadOnlyList<Member>> Entity() => _inquiry.QueryListAsync<Member>(
                InquirySql.Sql($"SELECT [member_key], m.[email_address] FROM [Members] m"));

            public Task<IReadOnlyList<Member>> Command() => _inquiry.QueryListAsync<Member>(
                new InquiryCommand("SELECT member_key, email_address, 1 AS Extra FROM Members"));
            """");

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("PostgreSql", """SELECT m."member_key", m."email_address" FROM "Members" m""")]
    [InlineData("MySql", "SELECT `member_key`, m.`email_address` FROM `Members` m")]
    [InlineData("MariaDb", "SELECT `member_key`, m.`email_address` FROM `Members` m")]
    [InlineData("Sqlite", "SELECT [member_key], \"email_address\" FROM Members")]
    [InlineData("Oracle", "SELECT \"member_key\" AS \"member_key\", m.email_address FROM Members m")]
    public async Task QuotedNamesOfEachDialectAreSilent(string dialect, string sql)
    {
        var diagnostics = await AnalyzeAsync(
            $"public Task<IReadOnlyList<Member>> Run() => _inquiry.QueryListAsync<Member>(new InquiryCommand(\"{sql.Replace("\"", "\\\"", StringComparison.Ordinal)}\"));",
            dialect);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task EntityIsCheckedAgainstItsColumnNamesNotItsPropertyNames()
    {
        var diagnostics = await AnalyzeAsync("""
            public Task<IReadOnlyList<Member>> Run() => _inquiry.QueryListAsync<Member>($"SELECT [MemberKey], [email_address] FROM [Members]");
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("has no column named 'member_key' for property 'MemberKey'", Message(diagnostic), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""public Task<IReadOnlyList<CodeClaim>> Run(string sql) => _inquiry.QueryListAsync<CodeClaim>(new InquiryCommand(sql));""")]
    [InlineData("""public Task<IReadOnlyList<CodeClaim>> Run(FormattableString sql) => _inquiry.QueryListAsync<CodeClaim>(sql);""")]
    [InlineData("""public Task<IReadOnlyList<CodeClaim>> Run(bool all) => _inquiry.QueryListAsync<CodeClaim>(all ? (FormattableString)$"SELECT [X]" : $"SELECT [Y]");""")]
    [InlineData("""public Task<IReadOnlyList<CodeClaim>> Run() => _inquiry.QueryListAsync<CodeClaim>($"SELECT * FROM [Claims]");""")]
    [InlineData("""public Task<IReadOnlyList<CodeClaim>> Run() => _inquiry.QueryListAsync<CodeClaim>($"SELECT c.*, [TMemberKey] FROM [Claims] c");""")]
    [InlineData("""public Task<IReadOnlyList<CodeClaim>> Run() => _inquiry.QueryListAsync<CodeClaim>($"UPDATE [Claims] SET [Used] = 1 OUTPUT inserted.* WHERE [Id] = 1");""")]
    [InlineData("""public Task<IReadOnlyList<CodeClaim>> Run() => _inquiry.QueryListAsync<CodeClaim>(new InquiryCommand("GetClaims", CommandType.StoredProcedure));""")]
    [InlineData("""public Task<IReadOnlyList<CodeClaim>> Run() => _inquiry.QueryListAsync<CodeClaim>(InquirySql.Sql($"GetClaims", CommandType.StoredProcedure));""")]
    [InlineData("""public Task<IReadOnlyList<CodeClaim>> Run() => _inquiry.QueryListAsync<CodeClaim>($"EXEC GetClaims");""")]
    [InlineData("""public Task<IReadOnlyList<CodeClaim>> Run() => _inquiry.QueryListAsync<CodeClaim>($"SELECT [TMemberKey] INTO #claims FROM [Claims]; SELECT * FROM #claims");""")]
    [InlineData("""public Task<IReadOnlyList<CodeClaim>> Run() => _inquiry.QueryListAsync<CodeClaim>($"DECLARE @n INT; SELECT @n = COUNT(*) FROM [Claims]");""")]
    [InlineData("""public Task<IReadOnlyList<CodeClaim>> Run() => _inquiry.QueryListAsync<CodeClaim>($"SELECT [TMemberKey] FROM [Claims] FOR JSON PATH");""")]
    [InlineData("""public Task<IReadOnlyList<CodeClaim>> Run() => _inquiry.QueryListAsync<CodeClaim>($"SELECT [Name] COLLATE Latin1_General_BIN FROM [Claims]");""")]
    [InlineData("""public Task<IReadOnlyList<CodeClaim>> Run() => _inquiry.QueryListAsync<CodeClaim>($"SELECT ([MemberKey]), [Outcome], [ExpiresUtc] FROM [Claims]");""")]
    [InlineData("""public Task<IReadOnlyList<CodeClaim>> Run() => _inquiry.QueryListAsync<CodeClaim>($"SELECT [A] + [B] MemberKey, [Outcome], [ExpiresUtc] FROM [Claims]");""")]
    [InlineData("""public Task<IReadOnlyList<CodeClaim>> Run() => _inquiry.QueryListAsync<CodeClaim>($"SELECT [MemberKey], [Outcome], [ExpiresUtc], COUNT(*) OVER () FROM [Claims]");""")]
    [InlineData("""public Task<Login?> Run() => _inquiry.QuerySingleOrDefaultAsync<Login>($"SELECT N'alice' AS [User], USER");""")]
    [InlineData("""public Task<Flag?> Run() => _inquiry.QuerySingleOrDefaultAsync<Flag>($"SELECT CASE WHEN [A] IS NULL THEN 1 ELSE 0 END IsNull FROM [Claims]");""")]
    [InlineData("""public Task<IReadOnlyList<Plain>> Run() => _inquiry.QueryListAsync<Plain>($"SELECT COUNT(*) FROM [Claims]");""")]
    [InlineData("""public Task<IReadOnlyList<T>> Run<T>() where T : class => _inquiry.QueryListAsync<T>($"SELECT COUNT(*) FROM [Claims]");""")]
    [InlineData("""public virtual FormattableString Sql() => $"SELECT [TMemberKey] FROM [Claims]"; public Task<IReadOnlyList<CodeClaim>> Run() => _inquiry.QueryListAsync<CodeClaim>(Sql());""")]
    public async Task UnknowableResultListsAreSilent(string member)
    {
        var diagnostics = await AnalyzeAsync(member);
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("Sqlite", "SELECT t.window AS Count FROM (SELECT 1 AS window) t")]
    [InlineData("SqlServer", "SELECT Amount Count FROM Accounts")]
    [InlineData("Sqlite", "INSERT INTO Items (Name) VALUES ('a') RETURNING rowid")]
    [InlineData("Sqlite", "SELECT [rowid] FROM Items")]
    [InlineData("Sqlite", "SELECT t.'Count' FROM Items t")]
    [InlineData("Sqlite", "SELECT a IS NOT DISTINCT FROM b AS Count FROM t")]
    [InlineData("MySql", "SELECT Count# the total\n, 1 AS Other FROM t")]
    [InlineData("SqlServer", "SELECT CAST(SCOPE_IDENTITY() AS int) Count\nCOMMIT TRANSACTION")]
    [InlineData("SqlServer", "SELECT STRING_AGG(Name, ',') WITHIN GROUP (ORDER BY Name) AS Count FROM Names")]
    [InlineData("PostgreSql", "SELECT percentile_cont(0.5) WITHIN GROUP (ORDER BY n) AS count FROM t")]
    [InlineData("SqlServer", "SELECT master..spt_values.number AS x, master..spt_values.Count FROM master..spt_values")]
    [InlineData("SqlServer", "SELECT Amount Limit, Amount Count FROM Accounts")]
    [InlineData("Sqlite", "SELECT Id window, Id Count FROM Panes")]
    [InlineData("SqlServer", "SELECT tz AS Count, SYSDATETIMEOFFSET() AT TIME ZONE Count FROM z")]
    [InlineData("MySql", "SELECT a AS Count, BINARY Count FROM t")]
    [InlineData("PostgreSql", "SELECT 1 AS \"Countaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"")]
    public async Task AmbiguousAliasesAndTruncatedNamesAreSilent(string dialect, string sql)
    {
        var diagnostics = await AnalyzeAsync(
            $"public Task<Total?> Run() => _inquiry.QuerySingleOrDefaultAsync<Total>(new InquiryCommand(\"{sql.Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)}\"));",
            dialect);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DeeplyNestedSqlIsSilentAndQuick()
    {
        var sql = "SELECT " + new string('(', 20000) + "1" + new string(')', 20000) + " AS Other";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var diagnostics = await AnalyzeAsync(
            $"public Task<Total?> Run() => _inquiry.QuerySingleOrDefaultAsync<Total>(new InquiryCommand(\"{sql}\"));");
        Assert.Empty(diagnostics);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"Analysis took {watch.Elapsed}.");
    }

    [Fact]
    public async Task UnknownOrAmbiguousDialectIsSilent()
    {
        var diagnostics = await AnalyzeAsync(
            """public Task<IReadOnlyList<CodeClaim>> Run() => _inquiry.QueryListAsync<CodeClaim>($"SELECT [TMemberKey] FROM [Claims]");""",
            dialect: null);
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("PostgreSql", 0)]
    [InlineData("SqlServer", 1)]
    [InlineData("MySql", 1)]
    [InlineData("Oracle", 1)]
    [InlineData("Sqlite", 1)]
    public async Task UnaliasedAggregateFollowsEachDialectsNamingRules(string dialect, int expected)
    {
        // PostgreSQL names count(*) "count", which matches the property. The others do not.
        var diagnostics = await AnalyzeAsync(
            """public Task<Total?> Run() => _inquiry.QuerySingleOrDefaultAsync<Total>(new InquiryCommand("SELECT COUNT(*) FROM Claims"));""",
            dialect);
        Assert.Equal(expected, diagnostics.Length);
    }

    [Fact]
    public async Task TwoColumnsMatchingOnePropertyReport()
    {
        var diagnostics = await AnalyzeAsync("""
            public Task<Total?> Run() => _inquiry.QuerySingleOrDefaultAsync<Total>($"SELECT COUNT(*) AS [Count], 0 AS count FROM [Claims]");
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("has 2 columns named 'Count' for property 'Count'", Message(diagnostic), StringComparison.Ordinal);
    }

    private static string[] Properties(ImmutableArray<Diagnostic> diagnostics)
        => diagnostics
            .Select(static diagnostic =>
            {
                var message = Message(diagnostic);
                var start = message.IndexOf("for property '", StringComparison.Ordinal) + "for property '".Length;
                return message[start..message.IndexOf('\'', start)];
            })
            .Order(StringComparer.Ordinal)
            .ToArray();
}
