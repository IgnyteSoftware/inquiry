namespace Inquiry.Generators.Tests;

/// <summary>
/// A store method parameter can be a reserved word written with <c>@</c>, such as <c>@event</c>. Roslyn
/// reports its name without the prefix, so the generated implementation must put the prefix back.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    public void ReservedWordParameterNamesCompile(string dialect)
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Paging;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TEvent")]
            public sealed class Event
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn]
                public string Name { get; set; } = string.Empty;

                [InquiryColumn]
                public int Rank { get; set; }
            }

            public partial class EventStore : InquiryStore<Event>
            {
                [InquiryInsert]
                public partial Task<int> InsertAsync(Event @event, CancellationToken @default = default);

                [InquiryInsert]
                public partial Task<int> InsertAllAsync(IEnumerable<Event> @params, CancellationToken cancellationToken = default);

                [InquiryUpdate]
                public partial Task<bool> UpdateAsync(Event @class, CancellationToken cancellationToken = default);

                [InquiryUpsert]
                public partial Task<int> UpsertAsync(Event @object, CancellationToken cancellationToken = default);

                [InquiryDelete]
                public partial Task<bool> DeleteAsync(long @long, CancellationToken cancellationToken = default);

                [InquirySelectOneByKey]
                public partial Task<Event?> GetAsync(long @this, CancellationToken cancellationToken = default);

                [InquirySelectAllByField(nameof(Event.Name))]
                public partial Task<IReadOnlyList<Event>> ByNameAsync(string @string, CancellationToken cancellationToken = default);

                [InquirySelectAllByField(nameof(Event.Name), nameof(Event.Rank))]
                public partial IAsyncEnumerable<Event> ByNameAndRankAsync(string @base, int @int, CancellationToken cancellationToken = default);

                [InquirySelectAllByField(nameof(Event.Name), OrderBy = "Id ASC", Paged = true)]
                public partial Task<IReadOnlyList<Event>> PageByNameAsync(string @case, int @for, int @while, CancellationToken cancellationToken = default);

                [InquirySelectAll(OrderBy = "Id ASC", Paged = true)]
                public partial Task<IReadOnlyList<Event>> PageAsync(int @do, int @if, CancellationToken cancellationToken = default);

                [InquiryKeysetPage("Id")]
                public partial Task<InquiryPage<Event, long>> KeysetAsync(long? @is, int @as, CancellationToken cancellationToken = default);

                [InquirySelectAllByPredicate]
                [InquiryWhere(nameof(Event.Id), Compare.In)]
                [InquiryWhere(nameof(Event.Rank), Compare.GreaterThan)]
                public partial Task<IReadOnlyList<Event>> ByIdsAsync(IReadOnlyList<long> @in, int @out, CancellationToken cancellationToken = default);

                [InquiryExists, InquiryWhere(nameof(Event.Name))]
                public partial Task<bool> ExistsAsync(string @new, CancellationToken cancellationToken = default);

                [InquiryDelete, InquiryWhere(nameof(Event.Id), Compare.In)]
                public partial Task<int> DeleteAllAsync(IEnumerable<long> @lock, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: dialect);

        AssertNoErrors(result);
        var text = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("EventStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains("InsertAsync(global::Demo.Event @event, global::System.Threading.CancellationToken @default)", text);
        Assert.Contains("GetAsync(long @this, global::System.Threading.CancellationToken cancellationToken)", text);
    }

    [Fact]
    public void ReservedWordProcedureParameterKeepsItsSqlName()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TEvent")]
            public sealed class Event
            {
                [InquiryKey]
                public long Id { get; set; }
            }

            public partial class EventStore : InquiryStore<Event>
            {
                [InquiryStoredProcedure("dbo.ArchiveEvents")]
                public partial Task<int> ArchiveAsync(int @event, string? @class, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");

        AssertNoErrors(result);
        var text = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("EventStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();

        // The C# value takes the prefix. The SQL parameter is still named after the plain word.
        Assert.Contains("ParameterName = \"@event\";", text);
        Assert.Contains("ParameterName = \"@class\";", text);
        Assert.DoesNotContain("@@event", text);
        Assert.Contains("(object?)@class ?? global::System.DBNull.Value", text);
    }

    [Fact]
    public void ReservedWordNamespaceSegmentDoesNotStopTheGenerator()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo.@internal;

            [InquiryTable("TItem")]
            public sealed class Item
            {
                [InquiryKey]
                public long Id { get; set; }
            }

            public partial class ItemStore : InquiryStore<Item>
            {
                [InquirySelectOneByKey]
                public partial Task<Item?> GetAsync(long id, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);

        AssertNoErrors(result);
        Assert.Contains(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ItemStore.InquiryStore.g.cs", StringComparison.Ordinal));
    }
}
