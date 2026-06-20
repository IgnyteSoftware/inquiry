using System;
using System.Linq;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Enum-as-string emission: <c>[InquiryEnumAsString]</c> makes the materializer read the column
/// with <c>Enum.Parse</c> and inserts bind the enum's member name with <c>DbType.String</c>.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string EnumAsStringSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        public enum Status { Active, Closed }

        [InquiryTable("TTicket")]
        public sealed class Ticket
        {
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }

            [InquiryColumn("Status"), InquiryEnumAsString]
            public Status Status { get; set; }

            [InquiryColumn("Prior"), InquiryEnumAsString]
            public Status? Prior { get; set; }
        }

        public partial class TicketStore : Inquiry.Stores.InquiryStore<Demo.Ticket>
        {
            [InquiryInsert]
            public partial Task<int> InsertAsync(Ticket ticket, CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void EnumAsStringMaterializerUsesEnumParse()
    {
        var result = RunGenerator(EnumAsStringSource);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("Ticket.InquiryEntity.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // Non-nullable enum-as-string parses the string directly.
        Assert.Contains("Status = global::System.Enum.Parse<global::Demo.Status>(reader.GetString(", text);
        // Nullable enum-as-string keeps the IsDBNull null-guard around the parse.
        Assert.Contains("(global::Demo.Status?)null : global::System.Enum.Parse<global::Demo.Status>(reader.GetString(", text);
    }

    [Fact]
    public void EnumAsStringInsertBindsMemberNameAsString()
    {
        var result = RunGenerator(EnumAsStringSource);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("TicketStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // String DbType, not the enum's underlying integer type.
        Assert.Contains("global::System.Data.DbType.String", text);
        // Non-nullable binds ToString(); nullable guards on HasValue.
        Assert.Contains(".ToString()", text);
        Assert.Contains(".HasValue ? (object)", text);
    }

    [Fact]
    public void EnumAsStringOnNonEnumReportsDiagnostic()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TThing")]
            public sealed class Thing
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn("Name"), InquiryEnumAsString]
                public string Name { get; set; } = string.Empty;
            }

            public partial class ThingStore : Inquiry.Stores.InquiryStore<Demo.Thing>
            {
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "INQ036");
        // The diagnostic must not cascade: no other error-severity diagnostics from broken codegen.
        Assert.All(
            result.GeneratorDiagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error),
            d => Assert.Equal("INQ036", d.Id));
    }

    private const string EnumAsStringInSource = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        public enum Status { Active, Closed }

        [InquiryTable("TTicket")]
        public sealed class Ticket
        {
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }

            [InquiryColumn("Status"), InquiryEnumAsString]
            public Status Status { get; set; }
        }

        public partial class TicketStore : Inquiry.Stores.InquiryStore<Demo.Ticket>
        {
            [InquirySelectAllByPredicate]
            [InquiryWhere("Status", Compare.In)]
            public partial Task<IReadOnlyList<Ticket>> ByStatusesAsync(IReadOnlyList<Status> statuses, CancellationToken cancellationToken = default);

            [InquirySelectAllByPredicate]
            [InquiryWhere("Status", Compare.NotIn)]
            public partial Task<IReadOnlyList<Ticket>> ExcludeStatusesAsync(IReadOnlyList<Status> statuses, CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void EnumAsStringInPredicateProjectsToMemberName()
    {
        var result = RunGenerator(EnumAsStringInSource);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("TicketStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // The IN collection must be projected through ToString() so the provider sees the member name (text), not the underlying integer.
        Assert.Contains("global::System.Linq.Enumerable.Select(statuses, static _e => _e.ToString())", text);
    }

    [Fact]
    public void EnumAsStringNotInPredicateProjectsToMemberName()
    {
        var result = RunGenerator(EnumAsStringInSource);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("TicketStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // The NOT IN collection must also be projected through ToString() (behind a null guard).
        Assert.Contains("InquiryInExpansion.ExpandNotIn(_c, \"@Status\", statuses is null ? null : global::System.Linq.Enumerable.Select(statuses, static _e => _e.ToString())", text);
    }

    [Fact]
    public void EnumAsStringInPredicateProjectsThroughArrayBindOnPostgreSql()
    {
        // The PostgreSQL '= ANY(array)' path binds the whole collection via InquiryArrayParameter.Bind;
        // the projection must reach it too, so the bound array carries member-name strings, not integers.
        var result = RunGenerator(EnumAsStringInSource, dialect: "PostgreSql");
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("TicketStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("InquiryArrayParameter.Bind(_c, \"@Status\", statuses is null ? null : global::System.Linq.Enumerable.Select(statuses, static _e => _e.ToString()))", text);
    }
}
