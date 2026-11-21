namespace Inquiry.Generators.Tests;

/// <summary>
/// #614: <c>[InquiryUpsert(MatchOn = ...)]</c> builds each dialect's key-upsert SQL with the unique
/// natural key in place of the entity key. The update branch never sets the key.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string MatchOnPrefSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Pref")]
        [InquiryIndex(nameof(MemberKey), nameof(TypeCode), IsUnique = true)]
        public sealed class Pref
        {
            [InquiryKey(IsGenerated = true)]
            public int Id { get; set; }

            [InquiryColumn]
            public int MemberKey { get; set; }

            [InquiryColumn(Length = 20)]
            public string TypeCode { get; set; } = "";

            [InquiryColumn]
            public bool Enabled { get; set; }
        }

        public partial class PrefStore : Inquiry.Stores.InquiryStore<Demo.Pref>
        {
            [InquiryUpsert(MatchOn = new[] { nameof(Pref.MemberKey), nameof(Pref.TypeCode) })]
            public partial Task<int> UpsertAsync(Pref pref, CancellationToken cancellationToken = default);

            [InquiryUpsert(MatchOn = new[] { nameof(Pref.MemberKey), nameof(Pref.TypeCode) })]
            public partial Task<Pref?> UpsertReturningAsync(Pref pref, CancellationToken cancellationToken = default);
        }
        """;

    private static string MatchOnPrefStoreText(string dialect, string source = MatchOnPrefSource)
    {
        var result = RunGenerator(source, dialect: dialect);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("PrefStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    [Fact]
    public void MatchOnUpsertUsesUpdateFirstLockedPatternOnSqlServer()
    {
        var text = MatchOnPrefStoreText("SqlServer");

        Assert.Contains(
            "private const string _sqlUpsert_UpsertAsync = \"BEGIN TRANSACTION; UPDATE [Pref] WITH (UPDLOCK, SERIALIZABLE) SET [Enabled] = @Enabled WHERE [MemberKey] = @MemberKey AND [TypeCode] = @TypeCode; IF @@ROWCOUNT = 0 INSERT INTO [Pref] ([MemberKey], [TypeCode], [Enabled]) VALUES (@MemberKey, @TypeCode, @Enabled); COMMIT TRANSACTION;\";",
            text);
        Assert.Contains(
            "private const string _sqlUpsertReturning_UpsertReturningAsync = \"DECLARE @_out TABLE ([Id] INT NULL, [MemberKey] INT NULL, [TypeCode] NVARCHAR(20) NULL, [Enabled] BIT NULL); BEGIN TRANSACTION; UPDATE [Pref] WITH (UPDLOCK, SERIALIZABLE) SET [Enabled] = @Enabled OUTPUT INSERTED.[Id], INSERTED.[MemberKey], INSERTED.[TypeCode], INSERTED.[Enabled] INTO @_out WHERE [MemberKey] = @MemberKey AND [TypeCode] = @TypeCode; IF @@ROWCOUNT = 0 INSERT INTO [Pref] ([MemberKey], [TypeCode], [Enabled]) OUTPUT INSERTED.[Id], INSERTED.[MemberKey], INSERTED.[TypeCode], INSERTED.[Enabled] INTO @_out VALUES (@MemberKey, @TypeCode, @Enabled); COMMIT TRANSACTION; SELECT [Id], [MemberKey], [TypeCode], [Enabled] FROM @_out\";",
            text);
        Assert.DoesNotContain("_sqlUpsert =", text);
        Assert.DoesNotContain("_sqlInsert", text);
        Assert.DoesNotContain("\"@Id\"", text);
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public void MatchOnUpsertTargetsTheNaturalKeyInOnConflict(string dialect)
    {
        var text = MatchOnPrefStoreText(dialect);

        Assert.Contains(
            "private const string _sqlUpsert_UpsertAsync = \"INSERT INTO \\\"Pref\\\" (\\\"MemberKey\\\", \\\"TypeCode\\\", \\\"Enabled\\\") VALUES (@MemberKey, @TypeCode, @Enabled) ON CONFLICT (\\\"MemberKey\\\", \\\"TypeCode\\\") DO UPDATE SET \\\"Enabled\\\" = @Enabled\";",
            text);
        Assert.Contains(
            "private const string _sqlUpsertReturning_UpsertReturningAsync = \"INSERT INTO \\\"Pref\\\" (\\\"MemberKey\\\", \\\"TypeCode\\\", \\\"Enabled\\\") VALUES (@MemberKey, @TypeCode, @Enabled) ON CONFLICT (\\\"MemberKey\\\", \\\"TypeCode\\\") DO UPDATE SET \\\"Enabled\\\" = @Enabled RETURNING \\\"Id\\\", \\\"MemberKey\\\", \\\"TypeCode\\\", \\\"Enabled\\\"\";",
            text);
    }

    [Fact]
    public void MatchOnUpsertReadsBackByNaturalKeyOnMySql()
    {
        var text = MatchOnPrefStoreText("MySql");

        Assert.Contains(
            "private const string _sqlUpsert_UpsertAsync = \"INSERT INTO `Pref` (`MemberKey`, `TypeCode`, `Enabled`) VALUES (@MemberKey, @TypeCode, @Enabled) ON DUPLICATE KEY UPDATE `Enabled` = VALUES(`Enabled`)\";",
            text);
        Assert.Contains(
            "private const string _sqlUpsertReturning_UpsertReturningAsync = \"INSERT INTO `Pref` (`MemberKey`, `TypeCode`, `Enabled`) VALUES (@MemberKey, @TypeCode, @Enabled) ON DUPLICATE KEY UPDATE `Enabled` = VALUES(`Enabled`); SELECT `Id`, `MemberKey`, `TypeCode`, `Enabled` FROM `Pref` WHERE `MemberKey` = @MemberKey AND `TypeCode` = @TypeCode\";",
            text);
        Assert.DoesNotContain("LAST_INSERT_ID", text);
    }

    [Fact]
    public void MatchOnUpsertUsesNativeReturningOnMariaDb()
    {
        var text = MatchOnPrefStoreText("MariaDb");

        Assert.Contains(
            "private const string _sqlUpsertReturning_UpsertReturningAsync = \"INSERT INTO `Pref` (`MemberKey`, `TypeCode`, `Enabled`) VALUES (@MemberKey, @TypeCode, @Enabled) ON DUPLICATE KEY UPDATE `Enabled` = VALUES(`Enabled`) RETURNING `Id`, `MemberKey`, `TypeCode`, `Enabled`\";",
            text);
    }

    [Fact]
    public void MatchOnUpsertMergesOnNaturalKeyOnOracleWithGeneratedKey()
    {
        var text = MatchOnPrefStoreText("Oracle");

        // Oracle bind names are hashed, so this pins the statement shape around them.
        Assert.Matches(
            @"_sqlUpsert_UpsertAsync = ""MERGE INTO Pref target USING \(SELECT (:\S+) AS k0, (:\S+) AS k1 FROM dual\) source ON \(target\.MemberKey = source\.k0 AND target\.TypeCode = source\.k1\) WHEN MATCHED THEN UPDATE SET Enabled = (:\S+) WHEN NOT MATCHED THEN INSERT \(MemberKey, TypeCode, Enabled\) VALUES \(\1, \2, \3\)"";",
            text);
        Assert.Matches(
            @"_sqlUpsertReturning_UpsertReturningAsync = ""BEGIN MERGE INTO Pref .* OPEN :rc FOR SELECT Id, MemberKey, TypeCode, Enabled FROM Pref WHERE MemberKey = :\S+ AND TypeCode = :\S+; END;"";",
            text);
    }

    [Fact]
    public void MatchOnUpsertWithAssignedKeyInsertsTheKeyButNeverSetsIt()
    {
        var source = MatchOnPrefSource.Replace("[InquiryKey(IsGenerated = true)]", "[InquiryKey]", StringComparison.Ordinal);
        var text = MatchOnPrefStoreText("PostgreSql", source);

        Assert.Contains(
            "private const string _sqlUpsert_UpsertAsync = \"INSERT INTO \\\"Pref\\\" (\\\"Id\\\", \\\"MemberKey\\\", \\\"TypeCode\\\", \\\"Enabled\\\") VALUES (@Id, @MemberKey, @TypeCode, @Enabled) ON CONFLICT (\\\"MemberKey\\\", \\\"TypeCode\\\") DO UPDATE SET \\\"Enabled\\\" = @Enabled\";",
            text);
    }

    [Fact]
    public void MatchOnUpsertAcceptsASingleColumnUniqueFlag()
    {
        var source = MatchOnPrefSource
            .Replace("[InquiryIndex(nameof(MemberKey), nameof(TypeCode), IsUnique = true)]", "", StringComparison.Ordinal)
            .Replace("[InquiryColumn(Length = 20)]", "[InquiryColumn(Length = 20, IsUnique = true)]", StringComparison.Ordinal)
            .Replace("MatchOn = new[] { nameof(Pref.MemberKey), nameof(Pref.TypeCode) }", "MatchOn = new[] { nameof(Pref.TypeCode) }", StringComparison.Ordinal);
        var text = MatchOnPrefStoreText("Sqlite", source);

        Assert.Contains("ON CONFLICT (\\\"TypeCode\\\") DO UPDATE SET \\\"MemberKey\\\" = @MemberKey, \\\"Enabled\\\" = @Enabled", text);
    }

    // Generated DDL drops a unique flag on an unbounded string where keys must be bounded, so the
    // flag gives no index to match on there.
    [Theory]
    [InlineData("SqlServer", true)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", true)]
    [InlineData("Oracle", true)]
    [InlineData("PostgreSql", false)]
    [InlineData("Sqlite", false)]
    public void UniqueFlagOnUnboundedStringGivesNoIndexWhereKeysMustBeBounded(string dialect, bool rejected)
    {
        var source = MatchOnPrefSource
            .Replace("[InquiryIndex(nameof(MemberKey), nameof(TypeCode), IsUnique = true)]", "", StringComparison.Ordinal)
            .Replace("[InquiryColumn(Length = 20)]", "[InquiryColumn(IsUnique = true)]", StringComparison.Ordinal)
            .Replace("MatchOn = new[] { nameof(Pref.MemberKey), nameof(Pref.TypeCode) }", "MatchOn = new[] { nameof(Pref.TypeCode) }", StringComparison.Ordinal);
        var result = RunGenerator(source, dialect: dialect);

        var errors = result.RunResult.Diagnostics.Where(static d => d.Id == "INQ121").ToArray();
        if (rejected)
        {
            Assert.Equal(2, errors.Length);
            Assert.Contains("no unique index covers exactly (TypeCode)", errors[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
        else
        {
            Assert.Empty(errors);
        }
    }

    [Theory]
    [InlineData("[InquiryIndex(nameof(MemberKey), nameof(TypeCode))]", "MatchOn = new[] { nameof(Pref.MemberKey), nameof(Pref.TypeCode) }", "no unique index covers exactly (MemberKey, TypeCode)")]
    [InlineData("[InquiryIndex(nameof(MemberKey), IsUnique = true)]", "MatchOn = new[] { nameof(Pref.MemberKey), nameof(Pref.TypeCode) }", "no unique index covers exactly (MemberKey, TypeCode)")]
    [InlineData("[InquiryIndex(nameof(MemberKey), nameof(TypeCode), nameof(Enabled), IsUnique = true)]", "MatchOn = new[] { nameof(Pref.MemberKey), nameof(Pref.TypeCode) }", "no unique index covers exactly (MemberKey, TypeCode)")]
    [InlineData("[InquiryIndex(nameof(MemberKey), nameof(TypeCode), IsUnique = true)]", "MatchOn = new[] { \"Missing\" }", "'Missing' is not a mapped property of 'Pref'")]
    [InlineData("[InquiryIndex(nameof(MemberKey), nameof(TypeCode), IsUnique = true)]", "MatchOn = new[] { nameof(Pref.MemberKey), nameof(Pref.MemberKey) }", "'MemberKey' is listed more than once")]
    [InlineData("[InquiryIndex(nameof(Id), IsUnique = true)]", "MatchOn = new[] { nameof(Pref.Id) }", "'Id' gets its value from the database")]
    public void InvalidMatchOnReportsINQ121(string index, string matchOn, string reason)
    {
        var source = MatchOnPrefSource
            .Replace("[InquiryIndex(nameof(MemberKey), nameof(TypeCode), IsUnique = true)]", index, StringComparison.Ordinal)
            .Replace("MatchOn = new[] { nameof(Pref.MemberKey), nameof(Pref.TypeCode) }", matchOn, StringComparison.Ordinal);
        var result = RunGenerator(source, dialect: "SqlServer");

        var errors = result.RunResult.Diagnostics.Where(static d => d.Id == "INQ121").ToArray();
        Assert.Equal(2, errors.Length);
        Assert.All(errors, error =>
        {
            Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, error.Severity);
            Assert.Contains(reason, error.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", true)]
    [InlineData("SqlServer", false)]
    [InlineData("PostgreSql", false)]
    [InlineData("Sqlite", false)]
    [InlineData("Oracle", false)]
    public void AnotherUniqueKeyRejectsMatchOnOnlyWhereAnyUniqueKeyConflicts(string dialect, bool rejected)
    {
        var source = MatchOnPrefSource.Replace(
            "[InquiryColumn]\n    public bool Enabled",
            "[InquiryColumn(Length = 40, IsUnique = true)]\n    public string Slug { get; set; } = \"\";\n\n    [InquiryColumn]\n    public bool Enabled",
            StringComparison.Ordinal);
        Assert.Contains("Slug", source, StringComparison.Ordinal);
        var result = RunGenerator(source, dialect: dialect);

        var errors = result.RunResult.Diagnostics.Where(static d => d.Id == "INQ121").ToArray();
        if (rejected)
        {
            Assert.Equal(2, errors.Length);
            Assert.Contains("also has a unique key on (Slug)", errors[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
        else
        {
            Assert.Empty(errors);
        }
    }

    [Theory]
    [InlineData("public int MemberKey", "public int? MemberKey", "'MemberKey' is nullable, or its converter stores a nullable value")]
    [InlineData("[InquiryColumn(Length = 20)]", "[InquiryColumn(Length = 20, DefaultExpression = \"'email'\", UseDatabaseDefault = true)]", "'TypeCode' gets its value from the database")]
    [InlineData("[InquiryColumn(Length = 20)]", "[InquiryModifiedBy]\n    [InquiryColumn(Length = 20)]", "'TypeCode' is an auditing column that the upsert stamps")]
    public void MatchOnColumnTheUpsertCannotMatchReportsINQ121(string find, string replace, string reason)
    {
        var source = MatchOnPrefSource.Replace(find, replace, StringComparison.Ordinal);
        Assert.Contains(replace, source, StringComparison.Ordinal);
        var result = RunGenerator(source, dialect: "PostgreSql");

        var errors = result.RunResult.Diagnostics.Where(static d => d.Id == "INQ121").ToArray();
        Assert.Equal(2, errors.Length);
        Assert.All(errors, error => Assert.Contains(reason, error.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }

    // ON DUPLICATE KEY UPDATE also fires on a primary-key conflict. With an assigned key, that would
    // update a row that has another natural key.
    [Theory]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", true)]
    [InlineData("SqlServer", false)]
    [InlineData("PostgreSql", false)]
    [InlineData("Sqlite", false)]
    [InlineData("Oracle", false)]
    public void KeyThatCanRepeatRejectsMatchOnOnlyWhereAnyUniqueKeyConflicts(string dialect, bool rejected)
    {
        foreach (var key in new[] { "[InquiryKey]", "[InquiryKey(UseDatabaseDefault = true, DefaultExpression = \"1\")]" })
        {
            AssertKeyRejection(MatchOnPrefSource.Replace("[InquiryKey(IsGenerated = true)]", key, StringComparison.Ordinal), dialect, rejected);
        }
    }

    private static void AssertKeyRejection(string source, string dialect, bool rejected)
    {
        var result = RunGenerator(source, dialect: dialect);

        var errors = result.RunResult.Diagnostics.Where(static d => d.Id == "INQ121").ToArray();
        if (rejected)
        {
            Assert.Equal(2, errors.Length);
            Assert.Contains("key 'Id' of 'Pref' is not AUTO_INCREMENT", errors[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
        else
        {
            Assert.Empty(errors);
        }
    }

    // A unique key over every match column and more can only conflict on the row the match index finds.
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public void UniqueKeyOverTheMatchColumnsDoesNotCompeteOnMySql(string dialect)
    {
        var source = MatchOnPrefSource.Replace(
            "[InquiryIndex(nameof(MemberKey), nameof(TypeCode), IsUnique = true)]",
            "[InquiryIndex(nameof(MemberKey), nameof(TypeCode), IsUnique = true)]\n[InquiryIndex(nameof(MemberKey), nameof(TypeCode), nameof(Enabled), IsUnique = true)]",
            StringComparison.Ordinal);

        MatchOnPrefStoreText(dialect, source);
    }

    private const string MatchOnTenantTagSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TenantTag")]
        [InquiryIndex(nameof(TenantId), nameof(Code), IsUnique = true)]
        public sealed class TenantTag
        {
            [InquiryKey]
            public int TenantId { get; set; }

            [InquiryKey]
            public int Id { get; set; }

            [InquiryColumn(Length = 20)]
            public string Code { get; set; } = "";

            [InquiryColumn(Length = 50)]
            public string Label { get; set; } = "";
        }

        public partial class TenantTagStore : Inquiry.Stores.InquiryStore<Demo.TenantTag>
        {
            [InquiryUpsert(MatchOn = new[] { nameof(TenantTag.TenantId), nameof(TenantTag.Code) })]
            public partial Task<int> UpsertAsync(TenantTag tag, CancellationToken cancellationToken = default);
        }
        """;

    // A natural key may share a column with a composite key. The update never sets either key column.
    [Fact]
    public void MatchOnMaySharePartOfACompositeKey()
    {
        var result = RunGenerator(MatchOnTenantTagSource, dialect: "PostgreSql");
        AssertNoErrors(result);
        var text = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("TenantTagStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();

        Assert.Contains(
            "private const string _sqlUpsert_UpsertAsync = \"INSERT INTO \\\"TenantTag\\\" (\\\"TenantId\\\", \\\"Id\\\", \\\"Code\\\", \\\"Label\\\") VALUES (@TenantId, @Id, @Code, @Label) ON CONFLICT (\\\"TenantId\\\", \\\"Code\\\") DO UPDATE SET \\\"Label\\\" = @Label\";",
            text);
    }

    [Fact]
    public void MatchOnThatHoldsTheWholeKeyReportsINQ121()
    {
        var source = MatchOnTenantTagSource
            .Replace("[InquiryIndex(nameof(TenantId), nameof(Code), IsUnique = true)]", "[InquiryIndex(nameof(TenantId), nameof(Id), nameof(Code), IsUnique = true)]", StringComparison.Ordinal)
            .Replace("MatchOn = new[] { nameof(TenantTag.TenantId), nameof(TenantTag.Code) }", "MatchOn = new[] { nameof(TenantTag.TenantId), nameof(TenantTag.Id), nameof(TenantTag.Code) }", StringComparison.Ordinal);
        var result = RunGenerator(source, dialect: "SqlServer");

        var error = Assert.Single(result.RunResult.Diagnostics, static d => d.Id == "INQ121");
        Assert.Contains("it contains the whole entity key", error.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
