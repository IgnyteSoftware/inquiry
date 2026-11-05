using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

/// <summary>
/// <c>[InquiryStoredProcedure]</c> is generated only for SQL Server and Oracle (#478). Other dialects,
/// RETURN status on Oracle, and Oracle read-backs that ODP.NET cannot bind report INQ114 at the method.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string ProcSupportHeader = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Item")]
        public sealed class Item
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryColumn("Name")]
            public string Name { get; set; } = string.Empty;
        }

        public partial class ProcSupportStore : InquiryStore<Item>
        {
            [InquirySelectAll]
            public partial Task<IReadOnlyList<Item>> AllAsync(CancellationToken cancellationToken = default);

        """;

    private const string ProviderNotSupportedReason =
        "Inquiry generates [InquiryStoredProcedure] methods only for SQL Server and Oracle. Remove the method, replace it with a supported query, or compile the store for a supported dialect.";

    private const string OracleReturnReason =
        "Oracle procedures have no RETURN status. Declare an OUT parameter in the procedure and set OutputParameter to its name instead of ReturnsValue = true.";

    public static TheoryData<string> UnsupportedProcedureDialects => new() { "Sqlite", "PostgreSql", "MySql", "MariaDb" };

    public static TheoryData<string, string> ProcedureFeatureDeclarations => new()
    {
        { "ListAsync", "[InquiryStoredProcedure(\"usp_List\")] public partial Task<IReadOnlyList<Item>> ListAsync(string name, CancellationToken cancellationToken = default);" },
        { "StreamAsync", "[InquiryStoredProcedure(\"usp_Stream\")] public partial IAsyncEnumerable<Item> StreamAsync(string name, CancellationToken cancellationToken = default);" },
        { "OneAsync", "[InquiryStoredProcedure(\"usp_One\")] public partial Task<Item?> OneAsync(long id, CancellationToken cancellationToken = default);" },
        { "TouchAsync", "[InquiryStoredProcedure(\"usp_Touch\")] public partial Task<int> TouchAsync(long id, CancellationToken cancellationToken = default);" },
        { "TotalAsync", "[InquiryStoredProcedure(\"usp_Total\", OutputParameter = \"total\")] public partial Task<int> TotalAsync(long id, CancellationToken cancellationToken = default);" },
        { "IncrementAsync", "[InquiryStoredProcedure(\"usp_Increment\")] public partial Task<int> IncrementAsync([InquiryParameter(IsInputOutput = true)] int value, CancellationToken cancellationToken = default);" },
        { "StatusAsync", "[InquiryStoredProcedure(\"usp_Status\", ReturnsValue = true)] public partial Task<int> StatusAsync(long id, CancellationToken cancellationToken = default);" },
        { "ReportAsync", "[InquiryStoredProcedure(\"usp_Report\")] public partial Task<(IReadOnlyList<Item>, IReadOnlyList<Item>)> ReportAsync(CancellationToken cancellationToken = default);" },
        { "LookupAsync", "[InquiryStoredProcedure(\"usp_Lookup\")] public partial Task<IReadOnlyList<Item>> LookupAsync([InquiryParameter(TvpTypeName = \"[dbo].[IdList]\")] IEnumerable<int> ids, CancellationToken cancellationToken = default);" },
    };

    private static string ProcSupportSource(string declaration) => ProcSupportHeader + "    " + declaration + "\n}\n";

    private static string GetProcSupportStore(GeneratorTestResult result)
        => Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("ProcSupportStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();

    private static void AssertSingleInq114(GeneratorTestResult result, string methodName, string dialect, string reason)
    {
        var diagnostic = Assert.Single(result.GeneratorDiagnostics, static d => d.Id == "INQ114");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(
            $"Stored-procedure method '{methodName}' cannot be generated for the '{dialect}' dialect: {reason}",
            diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        // Generator locations are rebuilt from cached data without a tree, so read the span from the input.
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal(methodName, result.Compilation.SyntaxTrees.First().GetText().ToString(span));
    }

    [Theory]
    [MemberData(nameof(UnsupportedProcedureDialects))]
    public void ProcSupport_UnsupportedDialectReportsInq114ForEveryFeature(string dialect)
    {
        foreach (var row in ProcedureFeatureDeclarations)
        {
            var methodName = (string)row[0];
            var declaration = (string)row[1];
            var result = RunGenerator(ProcSupportSource(declaration), dialect: dialect);

            AssertSingleInq114(result, methodName, dialect, ProviderNotSupportedReason);
            Assert.DoesNotContain(result.GeneratorDiagnostics, static d => d.Id is "INQ039" or "INQ051" or "INQ086" or "INQ005");

            // The rejected procedure gets no body; the ordinary select in the same store still emits.
            var text = GetProcSupportStore(result);
            Assert.DoesNotContain("usp_", text);
            Assert.Contains("AllAsync(", text);
        }
    }

    [Fact]
    public void ProcSupport_ShapeErrorPrecedesProviderError()
    {
        var source = ProcSupportSource(
            "[InquiryStoredProcedure(\"usp_X\", ReturnsValue = true)] public partial Task<string> XAsync(CancellationToken cancellationToken = default);");

        var result = RunGenerator(source, dialect: "Sqlite");

        Assert.Contains(result.GeneratorDiagnostics, static d => d.Id == "INQ051");
        Assert.DoesNotContain(result.GeneratorDiagnostics, static d => d.Id == "INQ114");
    }

    [Fact]
    public void ProcSupport_LoweredInq039DoesNotStubUnsupportedProcedure()
    {
        var source = ProcSupportSource(
            "[InquiryStoredProcedure(\"usp_Touch\")] public partial Task<int> TouchAsync(long id, CancellationToken cancellationToken = default);");

        var result = RunGenerator(source, dialect: "Sqlite", unsupportedOperationSeverity: ReportDiagnostic.Warn);

        AssertSingleInq114(result, "TouchAsync", "Sqlite", ProviderNotSupportedReason);
        var text = GetProcSupportStore(result);
        Assert.DoesNotContain("TouchAsync", text);
        Assert.DoesNotContain("NotSupportedException", text);
    }

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("Oracle")]
    public void ProcSupport_SupportedDialectAcceptsSharedFeatures(string dialect)
    {
        foreach (var row in ProcedureFeatureDeclarations)
        {
            var methodName = (string)row[0];
            if (dialect == "Oracle" && methodName is "StatusAsync" or "LookupAsync")
            {
                continue;
            }

            var result = RunGenerator(ProcSupportSource((string)row[1]), dialect: dialect);

            Assert.Empty(result.GeneratorDiagnostics);
            Assert.Contains(methodName + "(", GetProcSupportStore(result));
        }
    }

    [Fact]
    public void ProcSupport_OracleReturnsValueReportsInq114()
    {
        var source = ProcSupportSource(
            "[InquiryStoredProcedure(\"P_STATUS\", ReturnsValue = true)] public partial Task<int> StatusAsync(long id, CancellationToken cancellationToken = default);");

        var result = RunGenerator(source, dialect: "Oracle");

        AssertSingleInq114(result, "StatusAsync", "Oracle", OracleReturnReason);
        Assert.DoesNotContain("ParameterDirection.ReturnValue", GetProcSupportStore(result));
    }

    [Theory]
    [InlineData("string?", "Name")]
    [InlineData("byte[]?", "Payload")]
    public void ProcSupport_OracleVariableLengthOutputReportsInq114(string type, string outputName)
    {
        var source = ProcSupportSource(
            $"[InquiryStoredProcedure(\"P_OUT\", OutputParameter = \"{outputName}\")] public partial Task<{type}> ReadAsync(long id, CancellationToken cancellationToken = default);");

        var oracle = RunGenerator(source, dialect: "Oracle");
        AssertSingleInq114(oracle, "ReadAsync", "Oracle",
            $"OUTPUT parameter '{outputName}' is a string or binary value, and Oracle needs an explicit buffer size that OutputParameter cannot declare. Return the value through an IN OUT parameter with [InquiryParameter(IsInputOutput = true, Length = n)] instead.");

        Assert.Empty(RunGenerator(source, dialect: "SqlServer").GeneratorDiagnostics);
    }

    [Theory]
    [InlineData("System.Guid", "global::System.Guid")]
    [InlineData("System.DateTime", "global::System.DateTime")]
    [InlineData("bool", "bool")]
    public void ProcSupport_OracleNonNumericOutputReportsInq114(string type, string display)
    {
        var source = ProcSupportSource(
            $"[InquiryStoredProcedure(\"P_OUT\", OutputParameter = \"result\")] public partial Task<{type}> ReadAsync(long id, CancellationToken cancellationToken = default);");

        var oracle = RunGenerator(source, dialect: "Oracle");
        AssertSingleInq114(oracle, "ReadAsync", "Oracle",
            $"OUTPUT parameter 'result' has type '{display}'. Inquiry reads Oracle procedure OUTPUT and INOUT values only as numbers, or as strings and byte arrays through an IN OUT parameter with a Length. Return the value in a result set instead.");

        Assert.Empty(RunGenerator(source, dialect: "SqlServer").GeneratorDiagnostics);
    }

    [Theory]
    [InlineData("string", "")]
    [InlineData("string", ", Length = 0")]
    [InlineData("byte[]", "")]
    public void ProcSupport_OracleUnboundedInOutReportsInq114(string type, string lengthArgument)
    {
        var source = ProcSupportSource(
            $"[InquiryStoredProcedure(\"P_LABEL\")] public partial Task<{type}> LabelAsync([InquiryParameter(IsInputOutput = true{lengthArgument})] {type} label, CancellationToken cancellationToken = default);");

        var oracle = RunGenerator(source, dialect: "Oracle");
        AssertSingleInq114(oracle, "LabelAsync", "Oracle",
            "INOUT parameter 'label' is a string or binary value without a Length, and Oracle needs an explicit buffer size. Set [InquiryParameter(IsInputOutput = true, Length = n)] to the size the procedure declares.");

        Assert.Empty(RunGenerator(source, dialect: "SqlServer").GeneratorDiagnostics);
    }

    [Fact]
    public void ProcSupport_OracleBoundedInOutStampsDeclaredSize()
    {
        var source = ProcSupportSource(
            "[InquiryStoredProcedure(\"P_LABEL\")] public partial Task<string> LabelAsync([InquiryParameter(IsInputOutput = true, Length = 50)] string label, CancellationToken cancellationToken = default);");

        var result = RunGenerator(source, dialect: "Oracle");

        Assert.Empty(result.GeneratorDiagnostics);
        var text = GetProcSupportStore(result);
        Assert.Contains("_p0.Direction = global::System.Data.ParameterDirection.InputOutput;", text);
        Assert.Contains("_p0.Size = 50;", text);
    }

    [Fact]
    public void ProcSupport_OracleDateTimeInOutReportsInq114()
    {
        var source = ProcSupportSource(
            "[InquiryStoredProcedure(\"P_SHIFT\")] public partial Task<DateTime> ShiftAsync([InquiryParameter(IsInputOutput = true)] DateTime at, CancellationToken cancellationToken = default);");

        var oracle = RunGenerator(source, dialect: "Oracle");
        AssertSingleInq114(oracle, "ShiftAsync", "Oracle",
            "INOUT parameter 'at' has type 'global::System.DateTime'. Inquiry reads Oracle procedure OUTPUT and INOUT values only as numbers, or as strings and byte arrays through an IN OUT parameter with a Length. Return the value in a result set instead.");
    }
}
