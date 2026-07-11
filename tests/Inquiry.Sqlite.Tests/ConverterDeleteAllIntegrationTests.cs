using System.Collections;
using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

public readonly record struct DeleteId(long Value);

public sealed class DeleteIdConverter : IInquiryValueConverter<DeleteId, long>
{
    public static int ToProviderCalls { get; private set; }

    public static void Reset() => ToProviderCalls = 0;

    public long ToProvider(DeleteId value)
    {
        ToProviderCalls++;
        return value.Value;
    }

    public DeleteId FromProvider(long value) => new(value);
}

[InquiryTable("ConvertedDeleteItem")]
public sealed class ConvertedDeleteItem
{
    [InquiryKey(Converter = typeof(DeleteIdConverter))]
    public DeleteId? Id { get; set; }

    [InquiryColumn]
    public string Name { get; set; } = string.Empty;
}

public partial class ConvertedDeleteItemStore : InquiryStore<ConvertedDeleteItem>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(ConvertedDeleteItem item, CancellationToken ct = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<ConvertedDeleteItem>> AllAsync(CancellationToken ct = default);

    [InquiryDeleteAll]
    public partial Task<int> DeleteAllAsync(IEnumerable<DeleteId?>? ids, CancellationToken ct = default);
}

public sealed class ConverterDeleteAllIntegrationTests
{
    private const string Ddl = "CREATE TABLE ConvertedDeleteItem (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);";

    [Fact]
    public async Task DeleteAllProjectsOnceAndSkipsNullElements()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "converterdelete");
        var store = harness.GetRequiredService<ConvertedDeleteItemStore>();
        for (var id = 1; id <= 3; id++)
        {
            await store.InsertAsync(new ConvertedDeleteItem { Id = new DeleteId(id), Name = "item" + id });
        }

        DeleteIdConverter.Reset();
        var keys = new CountingEnumerable<DeleteId?>([new DeleteId(1), null, new DeleteId(3)]);
        Assert.Equal(2, await store.DeleteAllAsync(keys));
        Assert.Equal(1, keys.EnumerationCount);
        Assert.Equal(2, DeleteIdConverter.ToProviderCalls);

        var remaining = Assert.Single(await store.AllAsync());
        Assert.Equal(new DeleteId(2), remaining.Id);

        DeleteIdConverter.Reset();
        Assert.Equal(0, await store.DeleteAllAsync([]));
        Assert.Equal(0, DeleteIdConverter.ToProviderCalls);
        Assert.Equal(0, await store.DeleteAllAsync(null));
        Assert.Equal(0, DeleteIdConverter.ToProviderCalls);
    }

    private sealed class CountingEnumerable<T>(IEnumerable<T> values) : IEnumerable<T>
    {
        public int EnumerationCount { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;
            return values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
