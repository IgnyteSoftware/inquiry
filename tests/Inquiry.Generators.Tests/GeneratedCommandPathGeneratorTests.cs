using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void GeneratedOperationsUseStaticImmutableCommandDefinitionsIncludingEightArgumentState()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;
            namespace Demo;

            [InquiryTable("TProbe")]
            public sealed class Probe
            {
                [InquiryKey] public int Id { get; set; }
                [InquiryColumn] public int A { get; set; }
                [InquiryColumn] public int B { get; set; }
                [InquiryColumn] public int C { get; set; }
                [InquiryColumn] public int D { get; set; }
                [InquiryColumn] public int E { get; set; }
                [InquiryColumn] public int F { get; set; }
                [InquiryColumn] public int G { get; set; }
                [InquiryColumn] public int H { get; set; }
            }

            public partial class ProbeStore : InquiryStore<Probe>
            {
                [InquiryCount]
                public partial Task<long> CountAsync(CancellationToken cancellationToken = default);

                [InquirySelectOneByKey]
                public partial Task<Probe?> ByIdAsync(int id, CancellationToken cancellationToken = default);

                [InquirySelectAllByPredicate,
                 InquiryWhere("A", Compare.Equal), InquiryWhere("B", Compare.Equal),
                 InquiryWhere("C", Compare.Equal), InquiryWhere("D", Compare.Equal),
                 InquiryWhere("E", Compare.Equal), InquiryWhere("F", Compare.Equal),
                 InquiryWhere("G", Compare.Equal), InquiryWhere("H", Compare.Equal)]
                public partial Task<IReadOnlyList<Probe>> MatchAsync(
                    int a, int b, int c, int d, int e, int f, int g, int h,
                    CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("ProbeStore.InquiryStore.g.cs", StringComparison.Ordinal))
            .GetText().ToString();

        Assert.DoesNotContain("new global::Inquiry.Commands.InquiryCommand", text);
        Assert.Contains("InquiryGeneratedCommand<byte>", text);
        Assert.Contains("QueryGeneratedSingleOrDefaultAsync", text);
        Assert.Contains("int Arg7", text);
        Assert.Contains("Arg7: h", text);

        var root = CSharpSyntaxTree.ParseText(text).GetRoot();
        var commandCreations = root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .Where(static creation => creation.Type.ToString().Contains("InquiryGeneratedCommand", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(commandCreations);
        foreach (var creation in commandCreations)
        {
            var binder = creation.ArgumentList!.Arguments
                .Select(static argument => argument.Expression)
                .OfType<ParenthesizedLambdaExpressionSyntax>()
                .Single();
            Assert.Contains(binder.Modifiers, static modifier => modifier.IsKind(SyntaxKind.StaticKeyword));
        }
    }
}
