using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    // #62: a diagnostic/relation LocationData keys the incremental cache on FilePath + LineSpan, NOT the
    // absolute TextSpan. Editing text above an entity on an existing line (no newline change) shifts every
    // character offset below it but not the entity's own line/column, so its model must stay cached and its
    // materializer must not re-emit.
    private static string RegionWithRelationSource(string topComment) => $$"""
        {{topComment}}
        using System.Collections.Generic;
        using Inquiry.Entities;

        namespace Demo;

        [InquiryTable("TRegion")]
        public sealed class Region
        {
            [InquiryKey] public int Id { get; set; }

            [InquiryRelation(nameof(Territory.RegionId))]
            public IReadOnlyList<Territory> Territories { get; set; } = new List<Territory>();
        }

        [InquiryTable("TTerritory")]
        public sealed class Territory
        {
            [InquiryKey] public int Id { get; set; }
            [InquiryColumn] public int RegionId { get; set; }
        }
        """;

    [Fact]
    public void EditAboveEntityOnExistingLine_KeepsEntityModelCached()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp10);
        var v1 = CSharpSyntaxTree.ParseText(RegionWithRelationSource("// top"), parseOptions);
        var dialect = CSharpSyntaxTree.ParseText("[assembly: global::Inquiry.InquiryDialect(\"Sqlite\")]", parseOptions);

        var compilation = CSharpCompilation.Create(
            "InquiryLocationCacheTests",
            new[] { v1, dialect },
            GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new global::Inquiry.Sqlite.Analyzer.InquirySqliteGenerator().AsSourceGenerator() },
            parseOptions: parseOptions,
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        // First run primes the incremental cache.
        driver = driver.RunGenerators(compilation);

        // Lengthen the first-line comment: shifts every offset below it (the relation's TextSpan included)
        // but not a single line/column. Re-run the same driver against the replaced tree.
        var v2 = CSharpSyntaxTree.ParseText(RegionWithRelationSource("// top comment, now longer"), parseOptions);
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(v1, v2));

        var result = driver.GetRunResult().Results[0];

        // The entity model (which carries the relation's LocationData) stays cached despite the offset shift.
        AssertStepsCached(result, "InquiryEntities");
    }
}
