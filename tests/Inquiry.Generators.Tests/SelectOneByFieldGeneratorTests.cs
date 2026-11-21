namespace Inquiry.Generators.Tests;

/// <summary>
/// <c>[InquirySelectOneByField]</c> emission: a per-method SELECT limited to two rows on every dialect,
/// the soft-delete, global-filter and lock shapes of <c>[InquirySelectAllByField]</c>, a body that
/// throws on a second row, projections, and the field and return-type diagnostics.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string SelectOneByName = """
        [InquirySelectOneByField(nameof(Widget.Name))]
        public partial Task<Widget?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
        """;

    [Theory]
    [InlineData("Sqlite", "SELECT \\\"Id\\\", \\\"Name\\\", \\\"IsDeleted\\\" FROM \\\"TWidget\\\" WHERE \\\"Name\\\" = @Name AND \\\"IsDeleted\\\" = 0 LIMIT 2")]
    [InlineData("SqlServer", "SELECT TOP (2) [Id], [Name], [IsDeleted] FROM [TWidget] WHERE [Name] = @Name AND [IsDeleted] = 0")]
    [InlineData("PostgreSql", "SELECT \\\"Id\\\", \\\"Name\\\", \\\"IsDeleted\\\" FROM \\\"TWidget\\\" WHERE \\\"Name\\\" = @Name AND \\\"IsDeleted\\\" = FALSE LIMIT 2")]
    [InlineData("MySql", "SELECT `Id`, `Name`, `IsDeleted` FROM `TWidget` WHERE `Name` = @Name AND `IsDeleted` = 0 LIMIT 2")]
    [InlineData("MariaDb", "SELECT `Id`, `Name`, `IsDeleted` FROM `TWidget` WHERE `Name` = @Name AND `IsDeleted` = 0 LIMIT 2")]
    [InlineData("Oracle", "SELECT Id, Name, IsDeleted FROM TWidget WHERE Name = :iq1$Namexx$ce0862aa45f482 AND IsDeleted = 0 AND ROWNUM <= 2")]
    public void SelectOneByField_LimitsToTwoActiveRows(string dialect, string expectedSql)
    {
        var result = RunGenerator(WidgetStore(SelectOneByName), dialect: dialect);
        AssertNoErrors(result);
        var text = GetWidgetStore(result);

        Assert.Contains($"private const string _sqlSelectOneBy_Name_GetByNameAsync = \"{expectedSql}\";", text);
    }

    [Fact]
    public void SelectOneByField_ThrowsOnSecondRowNamingMethodAndField()
    {
        var result = RunGenerator(WidgetStore(SelectOneByName));
        AssertNoErrors(result);
        var text = GetWidgetStore(result);

        Assert.Contains("public async partial global::System.Threading.Tasks.Task<global::Demo.Widget?> GetByNameAsync(", text);
        Assert.Contains("capacityHint: 2).ConfigureAwait(false);", text);
        Assert.Contains(
            "if (_rows.Count > 1) throw new global::System.InvalidOperationException(\"Store method 'WidgetStore.GetByNameAsync' expects at most one row with a matching 'Name', but more than one row matched.\");",
            text);
        Assert.Contains("return _rows.Count == 0 ? null : _rows[0];", text);
    }

    [Fact]
    public void SelectOneByField_ParameterNamedLikeTheLocalCompiles()
    {
        var result = RunGenerator(WidgetStore("""
            [InquirySelectOneByField(nameof(Widget.Name))]
            public partial Task<Widget?> GetByNameAsync(string _rows, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);

        Assert.Contains("var _rows_ = await", GetWidgetStore(result));
    }

    [Fact]
    public void SelectOneByField_IncludeDeletedDropsSoftDeleteFilter()
    {
        var result = RunGenerator(WidgetStore("""
            [InquirySelectOneByField(nameof(Widget.Name), IncludeDeleted = true)]
            public partial Task<Widget?> GetAnyByNameAsync(string name, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetWidgetStore(result);

        Assert.Contains(
            "_sqlSelectOneBy_Name_GetAnyByNameAsync = \"SELECT \\\"Id\\\", \\\"Name\\\", \\\"IsDeleted\\\" FROM \\\"TWidget\\\" WHERE \\\"Name\\\" = @Name LIMIT 2\";",
            text);
    }

    [Fact]
    public void SelectOneByField_ComposesGlobalFilter()
    {
        var result = RunGenerator(AccountStore("""
            [InquirySelectOneByField(nameof(Account.Name), IncludeDeleted = true)]
            public partial Task<Account?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetAccountStore(result);

        // IncludeDeleted bypasses soft delete only; the global filter stays, as on [InquirySelectAllByField].
        Assert.Contains(
            "_sqlSelectOneBy_Name_GetByNameAsync = \"SELECT \\\"Id\\\", \\\"Name\\\", \\\"IsActive\\\" FROM \\\"TAccount\\\" WHERE \\\"Name\\\" = @Name AND \\\"IsActive\\\" = 1 LIMIT 2\";",
            text);
    }

    [Fact]
    public void SelectOneByField_BindsContextKeyFilter()
    {
        var result = RunGenerator(ActiveEntity
            .Replace("[InquiryColumn(\"IsActive\"), InquiryGlobalFilter]", "[InquiryColumn(\"TenantId\"), InquiryGlobalFilter(ContextKey = \"TenantId\")]", StringComparison.Ordinal)
            .Replace("public bool IsActive", "public long TenantId", StringComparison.Ordinal)
            + "\n\npublic partial class AccountStore : Inquiry.Stores.InquiryStore<Demo.Account>\n{\n" + """
            [InquirySelectOneByField(nameof(Account.Name))]
            public partial Task<Account?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
            """ + "\n}\n");
        AssertNoErrors(result);
        var text = GetAccountStore(result);

        Assert.Contains("\\\"TenantId\\\" = @__gf_TenantId LIMIT 2\";", text);
        Assert.Contains("__BindGlobalFilters(_cmd);", text);
    }

    [Fact]
    public void SelectOneByField_IgnoreFilterDropsOnlyTheNamedFilter()
    {
        var result = RunGenerator(PostStoreSource("""
            [InquirySelectOneByField("Title")]
            [InquiryIgnoreFilter("PublishGate")]
            public partial Task<Post?> AnyByTitleAsync(string title, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetNamedFilterPostStore(result);

        Assert.Contains("WHERE \\\"Title\\\" = @Title AND \\\"IsActive\\\" = 1 LIMIT 2\";", text);
    }

    [Fact]
    public void SelectOneByField_ExcludeReadsColumnSubset()
    {
        var result = RunGenerator(ExcludeStore("""
            [InquirySelectOneByField(nameof(Announcement.Title), Exclude = new[] { nameof(Announcement.Image) })]
            public partial Task<Announcement?> GetByTitleAsync(string title, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetExcludeStore(result);

        Assert.Equal(
            "const string _sqlSelectOneBy_Title_GetByTitleAsync = \"SELECT \"Id\", \"Title\", \"Body\", \"AuthorId\", \"Version\", \"IsDeleted\", \"IsPublished\" FROM \"TAnnouncement\" WHERE \"Title\" = @Title AND \"IsDeleted\" = 0 AND \"IsPublished\" = 1 LIMIT 2\"",
            GetConst(text, "_sqlSelectOneBy_Title_GetByTitleAsync"));
        Assert.Contains("Inquiry.QueryListAsync<global::Demo.Announcement, string, __InquiryReadMaterializer_GetByTitleAsync>(", text);
    }

    [Fact]
    public void SelectOneByField_ExcludeOnProjectionReportsINQ122()
    {
        var result = RunGenerator(ExcludeStore("""
            [InquirySelectOneByField(nameof(Announcement.Title), Exclude = new[] { nameof(Announcement.Image) })]
            public partial Task<AnnouncementTitle?> GetTitleAsync(string title, CancellationToken cancellationToken = default);
            """));

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ122");
    }

    [Theory]
    [InlineData("SqlServer", "SELECT TOP (2) [Id], [Name] FROM [TProduct] WITH (UPDLOCK, ROWLOCK) WHERE [Name] = @Name")]
    [InlineData("PostgreSql", "SELECT \\\"Id\\\", \\\"Name\\\" FROM \\\"TProduct\\\" WHERE \\\"Name\\\" = @Name LIMIT 2 FOR UPDATE")]
    [InlineData("MySql", "SELECT `Id`, `Name` FROM `TProduct` WHERE `Name` = @Name LIMIT 2 FOR UPDATE")]
    [InlineData("MariaDb", "SELECT `Id`, `Name` FROM `TProduct` WHERE `Name` = @Name LIMIT 2 FOR UPDATE")]
    [InlineData("Oracle", "SELECT Id, Name FROM TProduct WHERE Name = :iq1$Namexx$ce0862aa45f482 AND ROWNUM <= 2 FOR UPDATE")]
    public void SelectOneByField_LockModeKeepsTwoRowLimit(string dialect, string expectedSql)
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByField(nameof(Product.Name), LockMode = InquiryLockMode.Update)]
            public partial Task<Product?> GetByNameForUpdateAsync(string name, CancellationToken cancellationToken = default);
            """), dialect: dialect);
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains($"_sqlSelectOneBy_Name_GetByNameForUpdateAsync = \"{expectedSql}\";", text);
    }

    [Fact]
    public void SelectOneByField_LockModeOnSqliteReportsINQ039()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByField(nameof(Product.Name), LockMode = InquiryLockMode.Update)]
            public partial Task<Product?> GetByNameForUpdateAsync(string name, CancellationToken cancellationToken = default);
            """));

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ039");
    }

    [Fact]
    public void SelectOneByField_ReturnsProjection()
    {
        var result = RunGenerator(WidgetStore("""
            [InquirySelectOneByField(nameof(Widget.Name))]
            public partial Task<WidgetName?> GetNameAsync(string name, CancellationToken cancellationToken = default);
            }

            [InquiryProjection(typeof(Demo.Widget))]
            public sealed class WidgetName
            {
                [InquiryColumn("Name")]
                public string Name { get; set; } = string.Empty;
            """));
        AssertNoErrors(result);
        var text = GetWidgetStore(result);

        Assert.Contains(
            "_sqlProj_GetNameAsync = \"SELECT \\\"Name\\\" FROM \\\"TWidget\\\" WHERE \\\"Name\\\" = @Name AND \\\"IsDeleted\\\" = 0 LIMIT 2\";",
            text);
        Assert.Contains("Inquiry.QueryListAsync<global::Demo.WidgetName, string, global::Demo.WidgetNameInquiryProjectionStructMaterializer>(", text);
    }

    [Fact]
    public void SelectOneByField_UnknownFieldReportsINQ007()
    {
        var result = RunGenerator(WidgetStore("""
            [InquirySelectOneByField("Missing")]
            public partial Task<Widget?> GetByMissingAsync(string missing, CancellationToken cancellationToken = default);
            """));

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ007" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Missing", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Task<IReadOnlyList<Widget>>")]
    [InlineData("IAsyncEnumerable<Widget>")]
    [InlineData("Task<string>")]
    [InlineData("Task<Widget>")]
    [InlineData("Widget")]
    public void SelectOneByField_InvalidReturnTypeReportsINQ005(string returnType)
    {
        var result = RunGenerator(WidgetStore($$"""
            [InquirySelectOneByField(nameof(Widget.Name))]
            public partial {{returnType}} GetByNameAsync(string name, CancellationToken cancellationToken = default);
            """));

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ005");
    }

    [Fact]
    public void SelectOneByField_ParameterTypeMismatchReportsINQ006()
    {
        var result = RunGenerator(WidgetStore("""
            [InquirySelectOneByField(nameof(Widget.Name))]
            public partial Task<Widget?> GetByNameAsync(int name, CancellationToken cancellationToken = default);
            """));

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ006");
    }
}
