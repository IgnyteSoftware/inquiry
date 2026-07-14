using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void MySqlDefaultKeyInsertReturningCapturesDeclaredExpressionOnceInCollisionSafeVariable()
    {
        var result = RunGenerator(DefaultKeySource(
            "[InquiryKey(UseDatabaseDefault = true, Length = 255, DefaultExpression = \"(UUID())\")]",
            "string?",
            "[InquiryColumn] public string _inquiry_genkey { get; set; } = string.Empty;",
            includeInsertReturning: true), dialect: "MySql");

        AssertNoGeneratorErrors(result);
        var text = DefaultKeyStoreText(result);
        Assert.Contains("SET @'__inquiry.generated-key' = (UUID()); INSERT INTO `DefaultedItems` (`Id`, `Name`, `_inquiry_genkey`) VALUES (@'__inquiry.generated-key', @Name, @_inquiry_genkey); SELECT", text);
        Assert.Equal(3, Count(text, "@'__inquiry.generated-key'"));
        Assert.DoesNotContain("LAST_INSERT_ID", text);
        Assert.DoesNotContain(" := ", text);
    }

    [Fact]
    public void MySqlMissingDefaultExpressionDegradesOnlyReturningPathsThatNeedDefaultCapture()
    {
        var result = RunGenerator(DefaultKeySource(
            "[InquiryKey(UseDatabaseDefault = true, Length = 255)]",
            "string?",
            extraMethods: """
                [InquiryInsert] public partial Task<int> InsertAsync(DefaultedItem item, CancellationToken cancellationToken = default);
                [InquiryUpsert] public partial Task<int> UpsertAsync(DefaultedItem item, CancellationToken cancellationToken = default);
                [InquiryUpsert(ReturnEntity = true)] public partial Task<DefaultedItem?> UpsertReturningAsync(DefaultedItem item, CancellationToken cancellationToken = default);
                """,
            includeInsertReturning: true), dialect: "MySql", unsupportedOperationSeverity: ReportDiagnostic.Warn);

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ039" && d.Severity == DiagnosticSeverity.Warning);
        var text = DefaultKeyStoreText(result);
        Assert.Contains("private const string _sqlInsert =", text);
        Assert.Contains("private const string _sqlUpsert =", text);
        Assert.DoesNotContain("private const string _sqlInsertReturning =", text);
        Assert.Contains("throw new global::System.NotSupportedException", text);
    }

    [Fact]
    public void MySqlNonNullableDefaultKeyUpsertReturningDoesNotRequireDefaultExpression()
    {
        var result = RunGenerator(DefaultKeySource(
            "[InquiryKey(UseDatabaseDefault = true, Length = 255)]",
            "string",
            extraMethods: "[InquiryUpsert(ReturnEntity = true)] public partial Task<DefaultedItem?> UpsertReturningAsync(DefaultedItem item, CancellationToken cancellationToken = default);",
            includeInsertReturning: true),
            dialect: "MySql",
            unsupportedOperationSeverity: ReportDiagnostic.Warn);

        AssertNoGeneratorErrors(result);
        Assert.Single(result.RunResult.Diagnostics, static d => d.Id == "INQ039");
        var text = DefaultKeyStoreText(result);
        Assert.DoesNotContain("private const string _sqlInsertReturning =", text);
        Assert.Equal(1, Count(text, "throw new global::System.NotSupportedException"));
        Assert.Contains("VALUES (@Id, @Name) ON DUPLICATE KEY UPDATE `Name` = VALUES(`Name`); SELECT `Id`, `Name` FROM `DefaultedItems` WHERE `Id` = @Id", text);
        Assert.DoesNotContain("LAST_INSERT_ID", text);
        Assert.DoesNotContain("__inquiry.generated-key", text);
    }

    [Theory]
    [InlineData("[InquiryKey(UseDatabaseDefault = true, Length = 255, IsUnique = true)]", "")]
    [InlineData("[InquiryKey(UseDatabaseDefault = true, Length = 255)]", "[InquiryIndex(nameof(Id), IsUnique = true)]")]
    [InlineData("[InquiryKey(UseDatabaseDefault = true, Length = 255)]", "[InquiryIndex(nameof(Id), nameof(Code), IsUnique = true)]")]
    public void MySqlRedundantPrimaryKeyUniqueDeclarationsDoNotDegradeUpsertReturning(string keyAttribute, string tableAttribute)
    {
        var result = RunGenerator(DefaultKeySource(
            keyAttribute,
            "string",
            "[InquiryColumn(Length = 255)] public string Code { get; set; } = string.Empty;",
            extraMethods: "[InquiryUpsert(ReturnEntity = true)] public partial Task<DefaultedItem?> UpsertReturningAsync(DefaultedItem item, CancellationToken cancellationToken = default);",
            tableAttribute: tableAttribute), dialect: "MySql");

        AssertNoGeneratorErrors(result);
        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id == "INQ039");
        Assert.Contains("private const string _sqlUpsertReturning =", DefaultKeyStoreText(result));
    }

    [Theory]
    [InlineData("(UUID()); SELECT 1", "statement separator")]
    [InlineData("(UUID()) /* captured comment */", "comment")]
    [InlineData("(UUID()) # trailing comment", "comment")]
    [InlineData("@candidate", "user-variable")]
    [InlineData("(UUID()) ON UPDATE CURRENT_TIMESTAMP", "ON UPDATE")]
    [InlineData("DEFAULT(UUID())", "DEFAULT")]
    [InlineData("CONCAT(Name, UUID())", "mapped column 'Name'")]
    [InlineData("CONCAT(`Name`, UUID())", "mapped column 'Name'")]
    public void MySqlCaptureRejectsNonStandaloneDefaultExpressions(string expression, string expectedReason)
    {
        var result = RunGenerator(DefaultKeySource(
            $"[InquiryKey(UseDatabaseDefault = true, Length = 255, DefaultExpression = \"{expression.Replace("\"", "\\\"")}\")]",
            "string?",
            includeInsertReturning: true), dialect: "MySql");

        var diagnostic = Assert.Single(result.RunResult.Diagnostics, static d => d.Id == "INQ039");
        Assert.Contains("standalone scalar", diagnostic.GetMessage(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedReason, diagnostic.GetMessage(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private const string _sqlInsertReturning =", DefaultKeyStoreText(result));
    }

    [Fact]
    public void MySqlCustomGuidDefaultExpressionOverridesUuidFallback()
    {
        var result = RunGenerator(DefaultKeySource(
            "[InquiryKey(UseDatabaseDefault = true, DefaultExpression = \"CUSTOM_GUID()\")]",
            "System.Guid?",
            includeInsertReturning: true), dialect: "MySql");

        AssertNoGeneratorErrors(result);
        var text = DefaultKeyStoreText(result);
        Assert.Contains("SET @'__inquiry.generated-key' = CUSTOM_GUID();", text);
        Assert.DoesNotContain("SET @'__inquiry.generated-key' = UUID();", text);
    }

    [Fact]
    public void MySqlCaptureAllowsFunctionWhoseNameMatchesMappedProperty()
    {
        var result = RunGenerator(DefaultKeySource(
            "[InquiryKey(UseDatabaseDefault = true, Length = 255, DefaultExpression = \"UUID()\")]",
            "string?",
            "[InquiryColumn(Length = 255)] public string UUID { get; set; } = string.Empty;",
            includeInsertReturning: true), dialect: "MySql");

        AssertNoGeneratorErrors(result);
        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id == "INQ039");
        Assert.Contains("SET @'__inquiry.generated-key' = UUID();", DefaultKeyStoreText(result));
    }

    [Fact]
    public void MySqlCaptureAllowsCommaInsideNestedFunctionCall()
    {
        var result = RunGenerator(DefaultKeySource(
            "[InquiryKey(UseDatabaseDefault = true, Length = 255, DefaultExpression = \"CONCAT(UUID(), '-', UUID())\")]",
            "string?",
            includeInsertReturning: true), dialect: "MySql");

        AssertNoGeneratorErrors(result);
        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id == "INQ039");
        Assert.Contains("SET @'__inquiry.generated-key' = CONCAT(UUID(), '-', UUID());", DefaultKeyStoreText(result));
    }

    [Fact]
    public void MySqlCaptureRejectsTopLevelComma()
    {
        var result = RunGenerator(DefaultKeySource(
            "[InquiryKey(UseDatabaseDefault = true, Length = 255, DefaultExpression = \"UUID(), UUID()\")]",
            "string?",
            includeInsertReturning: true), dialect: "MySql");

        var diagnostic = Assert.Single(result.RunResult.Diagnostics, static d => d.Id == "INQ039");
        Assert.Contains("top-level comma", diagnostic.GetMessage(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MySqlCaptureRejectsUserVariableAssignmentOperator()
    {
        var result = RunGenerator(DefaultKeySource(
            "[InquiryKey(UseDatabaseDefault = true, Length = 255, DefaultExpression = \"(@candidate := UUID())\")]",
            "string?",
            includeInsertReturning: true), dialect: "MySql");

        var diagnostic = Assert.Single(result.RunResult.Diagnostics, static d => d.Id == "INQ039");
        Assert.Contains(":=", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void MySqlDdlOnlyDefaultExpressionDoesNotUseCaptureValidation()
    {
        var result = RunGenerator(DefaultKeySource(
            "[InquiryKey(UseDatabaseDefault = true, Length = 255, DefaultExpression = \"DEFAULT(UUID())\")]",
            "string?"), dialect: "MySql");

        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id == "INQ039");
    }

    [Theory]
    [InlineData("", "[InquiryColumn(IsUnique = true, Length = 255)]")]
    [InlineData("[InquiryIndex(nameof(Code), nameof(Name), IsUnique = true)]", "[InquiryColumn(Length = 255)]")]
    public void MySqlDefaultKeyUpsertReturningDegradesForEverySecondaryUniqueDeclaration(string tableAttribute, string codeAttribute)
    {
        var result = RunGenerator(DefaultKeySource(
            "[InquiryKey(UseDatabaseDefault = true, Length = 255)]",
            "string",
            $"{codeAttribute} public string Code {{ get; set; }} = string.Empty;",
            extraMethods: "[InquiryUpsert(ReturnEntity = true)] public partial Task<DefaultedItem?> UpsertReturningAsync(DefaultedItem item, CancellationToken cancellationToken = default);",
            tableAttribute: tableAttribute), dialect: "MySql");

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ039");
        Assert.DoesNotContain("private const string _sqlUpsertReturning =", DefaultKeyStoreText(result));
    }

    [Fact]
    public void MySqlCompositeDatabaseDefaultKeyInsertReturningDegrades()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;
            namespace Demo;
            [InquiryTable("CompositeItems")]
            public sealed class CompositeItem
            {
                [InquiryKey(UseDatabaseDefault = true, Length = 255)] public string? Tenant { get; set; }
                [InquiryKey] public int Id { get; set; }
            }
            public partial class CompositeItemStore : InquiryStore<CompositeItem>
            {
                [InquiryInsert(ReturnEntity = true)]
                public partial Task<CompositeItem?> InsertReturningAsync(CompositeItem item, CancellationToken cancellationToken = default);
                [InquiryUpsert(ReturnEntity = true)]
                public partial Task<CompositeItem?> UpsertReturningAsync(CompositeItem item, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "MySql");
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ039");
        var text = Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("CompositeItemStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.DoesNotContain("private const string _sqlInsertReturning =", text);
        Assert.DoesNotContain("private const string _sqlUpsertReturning =", text);
    }

    [Fact]
    public void MariaDbDefaultKeyUsesNativeReturningWithoutCaptureOrLastInsertId()
    {
        var result = RunGenerator(DefaultKeySource(
            "[InquiryKey(UseDatabaseDefault = true, Length = 255)]",
            "string?",
            extraMethods: "[InquiryUpsert(ReturnEntity = true)] public partial Task<DefaultedItem?> UpsertReturningAsync(DefaultedItem item, CancellationToken cancellationToken = default);",
            includeInsertReturning: true), dialect: "MariaDb");

        AssertNoGeneratorErrors(result);
        var text = DefaultKeyStoreText(result);
        Assert.Contains("INSERT INTO `DefaultedItems` (`Name`) VALUES (@Name) RETURNING `Id`, `Name`", text);
        Assert.Contains("INSERT INTO `DefaultedItems` (`Id`, `Name`) VALUES (@Id, @Name) ON DUPLICATE KEY UPDATE `Name` = VALUES(`Name`) RETURNING `Id`, `Name`", text);
        Assert.DoesNotContain("__inquiry.generated-key", text);
        Assert.DoesNotContain("LAST_INSERT_ID", text);
    }

    [Fact]
    public void MySqlAutoIncrementKeyOnlyUpsertAssignsLastInsertIdOnce()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;
            namespace Demo;
            [InquiryTable("Keys")]
            public sealed class KeyOnly { [InquiryKey(IsGenerated = true)] public int? Id { get; set; } }
            public partial class KeyOnlyStore : InquiryStore<KeyOnly>
            {
                [InquiryUpsert(ReturnEntity = true)]
                public partial Task<KeyOnly?> UpsertReturningAsync(KeyOnly item, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "MySql");
        AssertNoGeneratorErrors(result);
        var text = Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("KeyOnlyStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Equal(1, Count(text, "`Id` = LAST_INSERT_ID(`Id`)"));
    }

    private static string DefaultKeySource(
        string keyAttribute,
        string keyType,
        string extraProperty = "",
        string extraMethods = "",
        bool includeInsertReturning = false,
        string tableAttribute = "")
        => $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;
            namespace Demo;
            [InquiryTable("DefaultedItems")]
            {{tableAttribute}}
            public sealed class DefaultedItem
            {
                {{keyAttribute}} public {{keyType}} Id { get; set; }{{(keyType == "string" ? " = string.Empty;" : "")}}
                [InquiryColumn(Length = 255)] public string Name { get; set; } = string.Empty;
                {{extraProperty}}
            }
            public partial class DefaultedItemStore : InquiryStore<DefaultedItem>
            {
                {{(includeInsertReturning ? "[InquiryInsert(ReturnEntity = true)] public partial Task<DefaultedItem?> InsertReturningAsync(DefaultedItem item, CancellationToken cancellationToken = default);" : "")}}
                {{extraMethods}}
            }
            """;

    private static string DefaultKeyStoreText(GeneratorTestResult result)
        => Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("DefaultedItemStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();

    private static void AssertNoGeneratorErrors(GeneratorTestResult result)
    {
        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(result.Compilation.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);
    }

    private static int Count(string value, string fragment)
        => (value.Length - value.Replace(fragment, string.Empty).Length) / fragment.Length;
}
