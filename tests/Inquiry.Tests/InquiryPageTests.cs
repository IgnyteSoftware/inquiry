using Inquiry.Paging;

namespace Inquiry.Tests;

public sealed class InquiryPageTests
{
    private sealed class Item
    {
        public int Id { get; set; }
    }

    [Fact]
    public void ConstructorExposesItemsCursorAndHasMore()
    {
        var items = new[] { new Item { Id = 1 }, new Item { Id = 2 } };

        var page = new InquiryPage<Item, int>(items, nextCursor: 2, hasMore: true);

        Assert.Same(items, page.Items);
        Assert.Equal(2, page.NextCursor);
        Assert.True(page.HasMore);
    }

    [Fact]
    public void EmptyPageHasNullCursorAndNoMore()
    {
        var page = new InquiryPage<Item, int>(System.Array.Empty<Item>(), nextCursor: null, hasMore: false);

        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
        Assert.False(page.HasMore);
    }

    [Fact]
    public void SupportsValueTupleCursorForCompositeKeyset()
    {
        var page = new InquiryPage<Item, (string, int)>(
            new[] { new Item { Id = 1 } },
            nextCursor: ("Alpha", 1),
            hasMore: false);

        Assert.Equal(("Alpha", 1), page.NextCursor);
    }
}
