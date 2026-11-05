using System.Buffers;
using System.Runtime.InteropServices;

namespace Inquiry.Pipeline;

/// <summary>
/// Collects the rows of a buffered list read. Results of up to 16 rows, and results that fit a
/// capacity hint, use the <see cref="List{T}"/> exactly as before. A larger result moves to arrays
/// rented from <see cref="ArrayPool{T}.Shared"/> and is copied once into a list of the exact size,
/// so the read does not leave a trail of discarded growth arrays (#506).
/// </summary>
/// <remarks>
/// Callers keep the state in one <see cref="object"/> local, which holds either the list or the
/// pooled state. Async read loops hoist that local, so one reference keeps the state machine the
/// same size as the <see cref="List{T}"/> local it replaces.
/// </remarks>
internal static class InquiryBufferedList<T>
    where T : class
{
    // Matches the third List<T> growth step and the shared pool's smallest bucket.
    private const int PoolThreshold = 16;

    public static object Start(int capacityHint)
        => capacityHint > 0 ? new List<T>(capacityHint) : new List<T>();

    public static void Add(ref object? rows, T item)
    {
        if (rows is PooledRows pooled)
        {
            pooled.Append(item);
            return;
        }

        var list = (List<T>)rows!;
        if (list.Count == list.Capacity && list.Count >= PoolThreshold)
        {
            rows = new PooledRows(list, item);
            return;
        }

        list.Add(item);
    }

    public static List<T> Complete(object? rows)
        => rows is PooledRows pooled ? pooled.ToExactList() : (List<T>)rows!;

    /// <summary>Returns pooled storage after a failed read. Does nothing after <see cref="Complete"/>.</summary>
    public static void Release(object? rows)
    {
        if (rows is PooledRows pooled)
        {
            pooled.ReturnBuffer();
        }
    }

    private sealed class PooledRows
    {
        private readonly List<T> _list;
        private T[]? _buffer;
        private int _count;

        public PooledRows(List<T> list, T item)
        {
            _list = list;
            var buffer = ArrayPool<T>.Shared.Rent(list.Count * 2);
            CollectionsMarshal.AsSpan(list).CopyTo(buffer);
            _count = list.Count;
            buffer[_count++] = item;
            _buffer = buffer;
        }

        public void Append(T item)
        {
            var buffer = _buffer!;
            if (_count == buffer.Length)
            {
                buffer = Grow(buffer);
            }

            buffer[_count++] = item;
        }

        public List<T> ToExactList()
        {
            // The original list object is returned so callers that cast to List<T> keep working.
            _list.Clear();
            _list.Capacity = _count;
            _list.AddRange(new ReadOnlySpan<T>(_buffer, 0, _count));
            ReturnBuffer();
            return _list;
        }

        public void ReturnBuffer()
        {
            var buffer = _buffer;
            if (buffer is not null)
            {
                _buffer = null;
                Return(buffer, _count);
            }
        }

        private T[] Grow(T[] buffer)
        {
            var next = ArrayPool<T>.Shared.Rent((int)Math.Min((uint)buffer.Length * 2, (uint)Array.MaxLength));
            Array.Copy(buffer, next, _count);
            _buffer = next;
            Return(buffer, _count);
            return next;
        }

        // Clear the used slots so the pool does not keep entities reachable.
        private static void Return(T[] buffer, int count)
        {
            Array.Clear(buffer, 0, count);
            ArrayPool<T>.Shared.Return(buffer);
        }
    }
}
