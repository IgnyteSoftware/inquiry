namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    private const string ReturningProjection = """

        [InquiryProjection(typeof(Item))]
        public sealed class ItemScore
        {
            [InquiryColumn]
            public long Id { get; set; }

            [InquiryColumn]
            public int Score { get; set; }
        }

        """;

    private static string ReturningStore(string methods) => CompositionStore(methods).Replace(
        "public partial class ItemStore", ReturningProjection + "public partial class ItemStore", StringComparison.Ordinal);

    private const string IncrementScoreMethod = """
        [InquiryUpdate]
        [InquirySet("Score", "{Score} + 1")]
        [InquiryWhere("Id")]
        public partial Task<int?> IncrementAsync(long id, CancellationToken cancellationToken = default);
        """;

    [Theory]
    [InlineData("Sqlite", "UPDATE \\\"TItem\\\" SET \\\"Score\\\" = \\\"Score\\\" + 1 WHERE \\\"Id\\\" = @Id RETURNING \\\"Id\\\", \\\"Category\\\", \\\"Name\\\", \\\"Score\\\", \\\"State\\\"")]
    [InlineData("PostgreSql", "UPDATE \\\"TItem\\\" SET \\\"Score\\\" = \\\"Score\\\" + 1 WHERE \\\"Id\\\" = @Id RETURNING \\\"Id\\\", \\\"Category\\\", \\\"Name\\\", \\\"Score\\\", \\\"State\\\"")]
    [InlineData("SqlServer", "DECLARE @_out TABLE ([Id] BIGINT NULL, [Category] NVARCHAR(MAX) NULL, [Name] NVARCHAR(MAX) NULL, [Score] INT NULL, [State] NVARCHAR(MAX) NULL); UPDATE [TItem] SET [Score] = [Score] + 1 OUTPUT INSERTED.[Id], INSERTED.[Category], INSERTED.[Name], INSERTED.[Score], INSERTED.[State] INTO @_out WHERE [Id] = @Id; SELECT [Id], [Category], [Name], [Score], [State] FROM @_out")]
    [InlineData("Oracle", "DECLARE v_rowids SYS.ODCIVARCHAR2LIST; BEGIN UPDATE TItem SET Score = Score + 1 WHERE Id = :iq1$Idxxxx$30d4cf864d6e68 RETURNING ROWIDTOCHAR(ROWID) BULK COLLECT INTO v_rowids; OPEN :rc FOR SELECT Id, Category, Name, Score, State FROM TItem WHERE ROWID IN (SELECT CHARTOROWID(COLUMN_VALUE) FROM TABLE(v_rowids)); END;")]
    public void SetBasedUpdateReturnsTheUpdatedValue(string dialect, string expectedSql)
    {
        var result = RunGenerator(ReturningStore(IncrementScoreMethod), dialect: dialect);
        var text = GeneratedStoreText(result, "ItemStore.InquiryStore.g.cs");

        AssertCompiles(result);
        Assert.Contains($"private const string _sqlUpdateWhere_IncrementAsync = \"{expectedSql}\";", text);
        Assert.Contains("var _row = await Inquiry.QuerySingleOrDefaultAsync<global::Demo.Item, long, global::Demo.ItemInquiryEntityStructMaterializer>(_cmd, default, cancellationToken).ConfigureAwait(false);", text);
        Assert.Contains("return _row?.Score;", text);
        Assert.Contains("The updated row or value, read as it is after the update, or null when no row matches.", text);
    }

    // The issue's acceptance shape binds the criterion by name, which needs C# 11 nameof on a parameter.
    [Fact]
    public void NamedCriterionBindingReturnsTheUpdatedValue()
    {
        var result = RunGenerator(ReturningStore("""
            [InquiryUpdate]
            [InquirySet(nameof(Item.Score), "{Score} + 1")]
            [InquiryWhere(nameof(Item.Id), Parameter = nameof(id))]
            public partial Task<int?> IncrementAsync(long id, CancellationToken cancellationToken = default);
            """), languageVersion: Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp11);
        var text = GeneratedStoreText(result, "ItemStore.InquiryStore.g.cs");

        AssertCompiles(result);
        Assert.Contains("UPDATE \\\"TItem\\\" SET \\\"Score\\\" = \\\"Score\\\" + 1 WHERE \\\"Id\\\" = @Id RETURNING", text);
        Assert.Contains("return _row?.Score;", text);
    }

    [Fact]
    public void StampModifiedReturningUpdateStillWritesTheStamps()
    {
        var result = RunGenerator(StampedMemberSource("""
            [InquiryUpdate(StampModified = true)]
            [InquirySet(nameof(Member.Score), "{Score} + 1")]
            [InquiryWhere(nameof(Member.Id))]
            public partial Task<int?> AddScoreAsync(long id, CancellationToken cancellationToken = default);
            """));
        var text = GetMemberStore(result);

        AssertCompiles(result);
        Assert.Contains("UPDATE \\\"Member\\\" SET \\\"Score\\\" = \\\"Score\\\" + 1, \\\"TStamp\\\" = @TStamp, \\\"LastUpdatedBy\\\" = @LastUpdatedBy WHERE \\\"Id\\\" = @Id RETURNING", text);
    }

    [Fact]
    public void SqlServerOutputTableAvoidsABoundParameterName()
    {
        var result = RunGenerator(ReturningStore("""
            [InquiryUpdate]
            [InquirySet("Score", "{Score} + @_out")]
            [InquiryWhere("Id")]
            public partial Task<int?> IncrementAsync(int _out, long id, CancellationToken cancellationToken = default);
            """), dialect: "SqlServer");
        var text = GeneratedStoreText(result, "ItemStore.InquiryStore.g.cs");

        AssertCompiles(result);
        Assert.Contains("DECLARE @_out1 TABLE (", text);
        Assert.Contains("SET [Score] = [Score] + @_out OUTPUT INSERTED.[Id], INSERTED.[Category], INSERTED.[Name], INSERTED.[Score], INSERTED.[State] INTO @_out1 WHERE [Id] = @Id; SELECT [Id], [Category], [Name], [Score], [State] FROM @_out1", text);
    }

    [Fact]
    public void ValueReturnLocalsAvoidParameterNames()
    {
        var result = RunGenerator(ReturningStore("""
            [InquiryUpdate]
            [InquirySet("Score", "{Score} + 1")]
            [InquiryWhere("Id")]
            public partial Task<int?> IncrementAsync(long _row, CancellationToken cancellationToken = default);

            [InquiryUpdate]
            [InquirySet("Score", "{Score} + 1")]
            [InquiryWhere("Category", Compare.In)]
            [InquiryWhere("Name")]
            [InquiryWhere("Id", Compare.GreaterThan)]
            public partial Task<IReadOnlyList<int>> BumpAsync(IReadOnlyList<string> _rows, string _values, long _i, CancellationToken cancellationToken = default);
            """));

        AssertCompiles(result);
    }

    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public void SetBasedUpdateReturningIsRejectedWithoutUpdateReturning(string dialect)
    {
        var result = RunGenerator(ReturningStore(IncrementScoreMethod), dialect: dialect);

        var diagnostic = Assert.Single(result.RunResult.Diagnostics, static diagnostic => diagnostic.Id == "INQ039");
        Assert.Contains("IncrementAsync", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SetBasedUpdateReturnsEveryUpdatedRowAsAListOfValuesEntitiesOrProjections()
    {
        var result = RunGenerator(ReturningStore("""
            [InquiryUpdate]
            [InquirySet("Score", "{Score} + 1")]
            [InquiryWhere("Category")]
            public partial Task<IReadOnlyList<int>> BumpScoresAsync(string category, CancellationToken cancellationToken = default);

            [InquiryUpdate]
            [InquirySet("Score", "{Score} + 1")]
            [InquiryWhere("Category")]
            public partial Task<IReadOnlyList<Item>> BumpItemsAsync(string category, CancellationToken cancellationToken = default);

            [InquiryUpdate]
            [InquirySet("Score", "{Score} + 1")]
            [InquiryWhere("Category")]
            public partial Task<IReadOnlyList<ItemScore>> BumpProjectionsAsync(string category, CancellationToken cancellationToken = default);
            """), dialect: "SqlServer");
        var text = GeneratedStoreText(result, "ItemStore.InquiryStore.g.cs");

        AssertCompiles(result);
        Assert.Contains("_values.Add(_rows[_i].Score);", text);
        Assert.Contains("return Inquiry.QueryListAsync<global::Demo.Item, string, global::Demo.ItemInquiryEntityStructMaterializer>(_cmd, default, cancellationToken);", text);
        Assert.Contains("return Inquiry.QueryListAsync<global::Demo.ItemScore, string, global::Demo.ItemScoreInquiryProjectionStructMaterializer>(_cmd, default, cancellationToken);", text);
        // The OUTPUT table takes the entity's store types for the projected columns.
        Assert.Contains("private const string _sqlUpdateWhere_BumpProjectionsAsync = \"DECLARE @_out TABLE ([Id] BIGINT NULL, [Score] INT NULL); UPDATE [TItem] SET [Score] = [Score] + 1 OUTPUT INSERTED.[Id], INSERTED.[Score] INTO @_out WHERE [Category] = @Category; SELECT [Id], [Score] FROM @_out\";", text);
        Assert.DoesNotContain("_sqlProj_BumpProjectionsAsync", text);
    }

    [Fact]
    public void SqlServerReturningTableHonorsExplicitSqlType()
    {
        var result = RunGenerator("""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("Account")]
            public sealed class Account
            {
                [InquiryKey]
                public Guid Id { get; set; }

                [InquiryColumn(SqlType = "decimal(19,4)")]
                public decimal Balance { get; set; }
            }

            public partial class AccountStore : InquiryStore<Account>
            {
                [InquiryUpdate]
                [InquirySet("Balance", "{Balance} + @amount")]
                [InquiryWhere("Id")]
                public partial Task<decimal?> DepositAsync(decimal amount, Guid id, CancellationToken cancellationToken = default);
            }
            """, dialect: "SqlServer");
        var text = GeneratedStoreText(result, "AccountStore.InquiryStore.g.cs");

        AssertCompiles(result);
        Assert.Contains("DECLARE @_out TABLE ([Id] UNIQUEIDENTIFIER NULL, [Balance] decimal(19,4) NULL);", text);
    }

    [Fact]
    public void UniqueColumnCriteriaAllowASingleRowReturn()
    {
        var result = RunGenerator("""
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("Member")]
            public sealed class Member
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn(IsUnique = true)]
                public string Email { get; set; } = string.Empty;

                [InquiryColumn]
                public int Logins { get; set; }
            }

            public partial class MemberStore : InquiryStore<Member>
            {
                [InquiryUpdate]
                [InquirySet("Logins", "{Logins} + 1")]
                [InquiryWhere("Email")]
                public partial Task<Member?> RecordLoginAsync(string email, CancellationToken cancellationToken = default);
            }
            """);

        AssertCompiles(result);
    }

    [Theory]
    [InlineData("""
        [InquiryUpdate]
        [InquirySet("Score", "{Score} + 1")]
        [InquiryWhere("Category")]
        public partial Task<int?> IncrementAsync(string category, CancellationToken cancellationToken = default);
        """, "match at most one row")]
    [InlineData("""
        [InquiryUpdate]
        [InquirySet("Score", "{Score} + 1")]
        [InquiryWhere("Id")]
        [InquiryWhere("Category", Or = true)]
        public partial Task<Item?> IncrementAsync(long id, string category, CancellationToken cancellationToken = default);
        """, "match at most one row")]
    [InlineData("""
        [InquiryUpdate]
        [InquirySet("Score", "{Score} + 1")]
        [InquiryWhere("Category", OpenGroups = 1, Not = true)]
        [InquiryWhere("Id", CloseGroups = 1)]
        public partial Task<int?> IncrementAsync(string category, long id, CancellationToken cancellationToken = default);
        """, "match at most one row")]
    [InlineData("""
        [InquiryUpdate]
        [InquirySet("Score", "{Score} + 1")]
        [InquirySet("Name", "'x'")]
        [InquiryWhere("Id")]
        public partial Task<int?> IncrementAsync(long id, CancellationToken cancellationToken = default);
        """, "sets 2 columns")]
    [InlineData("""
        [InquiryUpdate]
        [InquirySet("Score", "{Score} + 1")]
        [InquiryWhere("Id")]
        public partial Task<long?> IncrementAsync(long id, CancellationToken cancellationToken = default);
        """, "does not match updated column 'Score'")]
    public void UnsupportedReturnShapesAreRejected(string method, string expectedMessage)
    {
        var result = RunGenerator(ReturningStore(method));

        var diagnostic = Assert.Single(result.RunResult.Diagnostics, static diagnostic => diagnostic.Id == "INQ096");
        Assert.Contains(expectedMessage, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SingleValueReturnMustBeNullable()
    {
        var result = RunGenerator(ReturningStore("""
            [InquiryUpdate]
            [InquirySet("Score", "{Score} + 1")]
            [InquiryWhere("Id")]
            public partial Task<long> IncrementAsync(long id, CancellationToken cancellationToken = default);
            """));

        Assert.Contains(result.RunResult.Diagnostics, static diagnostic => diagnostic.Id == "INQ005");
    }
}
