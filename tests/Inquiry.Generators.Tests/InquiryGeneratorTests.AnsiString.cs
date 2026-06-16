using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    // A [InquiryColumn(IsUnicode = false)] / [InquiryKey(IsUnicode = false)] string column must bind its
    // parameters as DbType.AnsiString (varchar) so varchar indexes seek instead of scan.
    [Fact]
    public void NonUnicodeStringColumn_BindsAnsiStringParameter()
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

            [InquiryTable("TWidget")]
            public sealed class Widget
            {
                [InquiryKey(IsUnicode = false)]
                public string Code { get; set; } = string.Empty;

                [InquiryColumn(IsUnicode = false)]
                public string Name { get; set; } = string.Empty;
            }

            public partial class WidgetStore : InquiryStore<Widget>
            {
                [InquirySelectOneByKey]
                public partial Task<Widget?> SelectByKeyAsync(string code, CancellationToken cancellationToken = default);

                [InquirySelectAllByField("Name")]
                public partial IAsyncEnumerable<Widget> SelectByNameAsync(string name, CancellationToken cancellationToken = default);

                [InquiryInsert]
                public partial Task<int> InsertAsync(Widget widget, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(errors);

        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var generatedText = generatedStore.GetText().ToString();

        // Both string columns are non-unicode → every bound string parameter is varchar (AnsiString),
        // and none is the nvarchar default.
        Assert.Contains("global::System.Data.DbType.AnsiString", generatedText);
        Assert.DoesNotContain("global::System.Data.DbType.String", generatedText);
    }

    // Backward compatibility: a default (unicode) string column keeps binding DbType.String (nvarchar).
    [Fact]
    public void UnicodeStringColumn_StillBindsStringParameter()
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

            [InquiryTable("TGadget")]
            public sealed class Gadget
            {
                [InquiryKey]
                public Guid Id { get; set; } = Guid.NewGuid();

                [InquiryColumn]
                public string Label { get; set; } = string.Empty;
            }

            public partial class GadgetStore : InquiryStore<Gadget>
            {
                [InquirySelectAllByField("Label")]
                public partial IAsyncEnumerable<Gadget> SelectByLabelAsync(string label, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(errors);

        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("GadgetStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var generatedText = generatedStore.GetText().ToString();

        Assert.Contains("global::System.Data.DbType.String", generatedText);
        Assert.DoesNotContain("global::System.Data.DbType.AnsiString", generatedText);
    }
}
