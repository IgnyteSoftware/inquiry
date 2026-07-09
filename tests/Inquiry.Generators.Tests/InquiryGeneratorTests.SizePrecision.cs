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

    // #107: the eager-load key binders bind their parent/child key parameters through the
    // InquiryParameter constructor (an inline array initializer, not a `_p` variable), so the declared
    // Size must be threaded as a constructor argument. A string-keyed [InquirySelectOneByKeyEager] must
    // therefore get the same stable sp_executesql signature as the plain SelectOneByKey path — not a
    // value-inferred one that re-pollutes the plan cache for the eager variant.
    private const string EagerKeySizeSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TDoc")]
        public sealed class Doc
        {
            [InquiryKey(Length = 64)]
            public string Code { get; set; } = string.Empty;

            [InquiryRelation(nameof(DocLine.DocCode))]
            public IReadOnlyList<DocLine> Lines { get; set; } = new List<DocLine>();
        }

        [InquiryTable("TDocLine")]
        public sealed class DocLine
        {
            [InquiryKey]
            public Guid Id { get; set; }

            [InquiryColumn(Length = 64)]
            public string DocCode { get; set; } = string.Empty;

            [InquiryColumn]
            public string Text { get; set; } = string.Empty;
        }

        public partial class DocStore : InquiryStore<Doc>
        {
            [InquirySelectOneByKeyEager]
            public partial Task<Doc?> GetWithLinesAsync(string code, CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void SqlServer_EmitsSizeOnEagerLoadStringKeyBinders()
    {
        var result = RunGenerator(EagerKeySizeSource, dialect: "SqlServer");
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(errors);

        var generatedText = GetDocStoreText(result);

        // The eager key binders build their parameters inline, so the declared length is threaded as a
        // constructor argument (size: 64) rather than a `.Size = 64;` statement.
        Assert.Contains("size: 64", generatedText);
    }

    // The decimal branch of the eager-key suffix emits a bare `precision: 18, scale: 2` constructor
    // argument (the byte-range is guaranteed by the <= 38 gate, mirroring AppendSizePrecision). A
    // declared-decimal key on an eager load must carry it for the same plan-cache parity.
    [Fact]
    public void SqlServer_EmitsPrecisionScaleOnEagerLoadDecimalKeyBinders()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TInvoice")]
            public sealed class Invoice
            {
                [InquiryKey(Precision = 18, Scale = 2)]
                public decimal Number { get; set; }

                [InquiryRelation(nameof(InvoiceLine.InvoiceNumber))]
                public IReadOnlyList<InvoiceLine> Lines { get; set; } = new List<InvoiceLine>();
            }

            [InquiryTable("TInvoiceLine")]
            public sealed class InvoiceLine
            {
                [InquiryKey]
                public Guid Id { get; set; }

                [InquiryColumn(Precision = 18, Scale = 2)]
                public decimal InvoiceNumber { get; set; }
            }

            public partial class InvoiceStore : InquiryStore<Invoice>
            {
                [InquirySelectOneByKeyEager]
                public partial Task<Invoice?> GetWithLinesAsync(decimal number, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(errors);

        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("InvoiceStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var generatedText = generatedStore.GetText().ToString();

        Assert.Contains("precision: 18, scale: 2", generatedText);
    }

    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    public void NonSqlServerDialects_EmitNoSizeOnEagerLoadStringKeyBinders(string dialect)
    {
        var result = RunGenerator(EagerKeySizeSource, dialect: dialect);
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(errors);

        var generatedText = GetDocStoreText(result);

        Assert.DoesNotContain("size: ", generatedText);
    }

    // #102: Compare.In / NotIn list elements are expanded by the InquiryInExpansion runtime helper, which
    // builds one DbParameter per element. On SQL Server a declared-length string/decimal IN column must
    // thread its Size/Precision/Scale into those element parameters too, or `Name IN ('ab')` vs
    // `IN ('abcd')` re-introduce the per-value-length sp_executesql signatures #56 fixed for scalars.
    private const string InListSizeSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TTag")]
        public sealed class Tag
        {
            [InquiryKey] public Guid Id { get; set; }
            [InquiryColumn(Length = 64)] public string Name { get; set; } = string.Empty;
            [InquiryColumn(Precision = 18, Scale = 2)] public decimal Weight { get; set; }
        }

        public partial class TagStore : InquiryStore<Tag>
        {
            [InquirySelectAllByPredicate]
            [InquiryWhere("Name", Compare.In)]
            public partial Task<IReadOnlyList<Tag>> InNamesAsync(IReadOnlyList<string> name, CancellationToken cancellationToken = default);

            [InquirySelectAllByPredicate]
            [InquiryWhere("Name", Compare.NotIn)]
            public partial Task<IReadOnlyList<Tag>> NotInNamesAsync(IReadOnlyList<string> name, CancellationToken cancellationToken = default);

            [InquirySelectAllByPredicate]
            [InquiryWhere("Weight", Compare.In)]
            public partial Task<IReadOnlyList<Tag>> InWeightsAsync(IReadOnlyList<decimal> weight, CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void SqlServer_ThreadsSizePrecisionIntoNotInListElements()
    {
        var result = RunGenerator(InListSizeSource, dialect: "SqlServer");
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(errors);

        var generatedText = GetTagStoreText(result);

        // #69: IN predicates now use TVP binding (no expansion, no Size threading needed).
        Assert.Contains("InquiryTvpParameter.Bind(_c", generatedText);
        // NOT IN still uses sentinel expansion with Size/Precision threading.
        Assert.Contains("ExpandNotIn(_c", generatedText);
        Assert.Contains("size: 64", generatedText);
    }

    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    public void NonSqlServerDialects_ThreadNoSizePrecisionIntoInListElements(string dialect)
    {
        var result = RunGenerator(InListSizeSource, dialect: dialect);
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(errors);

        var generatedText = GetTagStoreText(result);

        Assert.DoesNotContain("size: ", generatedText);
        Assert.DoesNotContain("precision: ", generatedText);
    }

    private static string GetTagStoreText(GeneratorTestResult result)
    {
        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("TagStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return generatedStore.GetText().ToString();
    }

    private static string GetDocStoreText(GeneratorTestResult result)
    {
        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("DocStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return generatedStore.GetText().ToString();
    }

    private static string GetProductStoreText(GeneratorTestResult result)
    {
        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("ProductStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return generatedStore.GetText().ToString();
    }
}
