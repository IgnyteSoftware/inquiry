using Inquiry.Generators.Infrastructure;
using System.Collections.Immutable;

namespace Inquiry.Generators.Tests;

/// <summary>
/// A default <see cref="EquatableArray{T}"/> must behave like an empty one everywhere, not just in
/// <c>Count</c>/<c>AsImmutableArray</c> — indexing it must report an out-of-range access rather than
/// dereferencing the missing backing array.
/// </summary>
public sealed class EquatableArrayTests
{
    [Fact]
    public void DefaultInstanceIsEmpty()
    {
        var array = default(EquatableArray<string>);

        Assert.Equal(0, array.Count);
        Assert.True(array.AsImmutableArray().IsEmpty);
        Assert.True(array.Equals(EquatableArray<string>.Empty));
    }

    [Fact]
    public void IndexingDefaultInstanceThrowsOutOfRange()
    {
        var array = default(EquatableArray<string>);

        Assert.Throws<IndexOutOfRangeException>(() => array[0]);
    }

    [Fact]
    public void IndexingReturnsElements()
    {
        var array = new EquatableArray<string>(ImmutableArray.Create("a", "b"));

        Assert.Equal("a", array[0]);
        Assert.Equal("b", array[1]);
    }
}
