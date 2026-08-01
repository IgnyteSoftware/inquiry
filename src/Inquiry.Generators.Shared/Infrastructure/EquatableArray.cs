using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Inquiry.Generators.Infrastructure;

/// <summary>
/// An <see cref="ImmutableArray{T}"/> wrapper with structural (sequence) value equality. Models that
/// flow through the incremental generator pipeline must be value-equatable for caching to work;
/// <see cref="ImmutableArray{T}"/> uses reference equality, so collections are wrapped in this type.
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _array;

    public EquatableArray(ImmutableArray<T> array) => _array = array;

    public static EquatableArray<T> Empty => new(ImmutableArray<T>.Empty);

    public int Count => _array.IsDefault ? 0 : _array.Length;

    public T this[int index] => AsImmutableArray()[index];

    public ImmutableArray<T> AsImmutableArray() => _array.IsDefault ? ImmutableArray<T>.Empty : _array;

    public ImmutableArray<T>.Enumerator GetEnumerator() => AsImmutableArray().GetEnumerator();

    public bool Equals(EquatableArray<T> other)
    {
        var left = AsImmutableArray();
        var right = other.AsImmutableArray();
        if (left.Length != right.Length)
        {
            return false;
        }

        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < left.Length; i++)
        {
            if (!comparer.Equals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            var comparer = EqualityComparer<T>.Default;
            foreach (var item in AsImmutableArray())
            {
                hash = (hash * 31) + (item is null ? 0 : comparer.GetHashCode(item));
            }

            return hash;
        }
    }

    public static implicit operator EquatableArray<T>(ImmutableArray<T> array) => new(array);
}
