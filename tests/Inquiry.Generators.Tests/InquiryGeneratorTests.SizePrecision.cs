using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    // Entity with a declared-length string, a declared-precision decimal, and an undeclared-length
    // string. Exercises both an insert binder and predicate (by-field) binders.
    private const string SizePrecisionSource = """
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
            public Guid Id { get; set; }

            [InquiryColumn(Length = 64)]
            public string Name { get; set; } = string.Empty;

            [InquiryColumn(Precision = 18, Scale = 2)]
            public decimal Price { get; set; }

            [InquiryColumn]
            public string Description { get; set; } = string.Empty;
        }

        public partial class ProductStore : InquiryStore<Product>
        {
            [InquirySelectAllByField("Name")]
            public partial IAsyncEnumerable<Product> SelectByNameAsync(string name, CancellationToken cancellationToken = default);

            [InquirySelectAllByField("Price")]
            public partial IAsyncEnumerable<Product> SelectByPriceAsync(decimal price, CancellationToken cancellationToken = default);

            [InquiryInsert]
            public partial Task<int> InsertAsync(Product product, CancellationToken cancellationToken = default);
        }
        """;

    // SQL Server keys its sp_executesql plan cache on the parameter signature, so generated binders emit
    // Size (declared-length string) and Precision/Scale (declared decimal) to keep that signature stable
    // across value lengths. An undeclared-length string is left to provider inference — no Size, no
    // invented default that could truncate or force a scan.
    [Fact]
    public void SqlServer_EmitsSizeAndPrecisionForDeclaredColumns()
    {
        var result = RunGenerator(SizePrecisionSource, dialect: "SqlServer");
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(errors);

        var generatedText = GetProductStoreText(result);

        // Declared-length string → Size; declared decimal → Precision + Scale.
        Assert.Contains(".Size = 64;", generatedText);
        Assert.Contains(".Precision = 18;", generatedText);
        Assert.Contains(".Scale = 2;", generatedText);

        // The undeclared-length Description column must NOT get a Size (no invented/zero default).
        Assert.DoesNotContain(".Size = 0;", generatedText);
    }

    // The other dialects key their plan cache on SQL text, so they emit no Size/Precision — keeping the
    // generated binders (and their snapshots) byte-identical to before.
    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    public void NonSqlServerDialects_EmitNoSizeOrPrecision(string dialect)
    {
        var result = RunGenerator(SizePrecisionSource, dialect: dialect);
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(errors);

        var generatedText = GetProductStoreText(result);

        Assert.DoesNotContain(".Size = ", generatedText);
        Assert.DoesNotContain(".Precision = ", generatedText);
        Assert.DoesNotContain(".Scale = ", generatedText);
    }

    // Size must NOT be emitted on value-write binders (insert/update): SqlClient silently truncates an
    // over-length value when the parameter has a Size, turning a loud server error into silent data loss.
    // An insert-only store over a declared-length column therefore emits no Size at all.
    [Fact]
    public void SqlServer_DoesNotEmitSizeOnWriteBinders()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TLabel")]
            public sealed class Label
            {
                [InquiryKey] public Guid Id { get; set; }
                [InquiryColumn(Length = 64)] public string Text { get; set; } = string.Empty;
                [InquiryColumn(Precision = 18, Scale = 2)] public decimal Weight { get; set; }
            }

            public partial class LabelStore : InquiryStore<Label>
            {
                [InquiryInsert] public partial Task<int> InsertAsync(Label label, CancellationToken cancellationToken = default);
                [InquiryUpdate] public partial Task<bool> UpdateAsync(Label label, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(errors);

        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("LabelStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var generatedText = generatedStore.GetText().ToString();

        // The Update binder matches by key (predicate) so its key parameter may carry Size, but the Id key
        // is a Guid; no string/decimal value parameter on the write path gets Size/Precision.
        Assert.DoesNotContain(".Size = ", generatedText);
        Assert.DoesNotContain(".Precision = ", generatedText);
    }

    private static string GetProductStoreText(GeneratorTestResult result)
    {
        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("ProductStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return generatedStore.GetText().ToString();
    }
}
