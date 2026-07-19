using System.Collections;

namespace Inquiry.Commands;

internal sealed class InquiryBatchChunkReader<T> : IDisposable
{
    private readonly int _maxBatchSize;
    private readonly IReadOnlyList<T>? _items;
    private readonly InquiryBatchChunkView<T>? _view;
    private readonly IEnumerator<T>? _enumerator;
    private readonly List<T>? _buffer;
    private readonly CancellationToken _cancellationToken;
    private int _offset;
    private int _disposed;

    internal InquiryBatchChunkReader(IEnumerable<T> items, int maxBatchSize, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (maxBatchSize <= 0) throw new ArgumentOutOfRangeException(nameof(maxBatchSize));
        cancellationToken.ThrowIfCancellationRequested();

        _maxBatchSize = maxBatchSize;
        _cancellationToken = cancellationToken;
        if (items is IReadOnlyList<T> list)
        {
            _items = list;
            _view = new InquiryBatchChunkView<T>();
        }
        else
        {
            _enumerator = items.GetEnumerator();
            _buffer = new List<T>(Math.Min(maxBatchSize, InquiryOptions.DefaultMaxBatchSize));
        }
    }

    internal bool MoveNext(out IReadOnlyList<T> chunk)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (_items is not null)
        {
            if (_offset >= _items.Count)
            {
                chunk = Array.Empty<T>();
                return false;
            }

            var count = Math.Min(_maxBatchSize, _items.Count - _offset);
            if (_offset == 0 && count == _items.Count)
            {
                _offset = count;
                chunk = _items;
                return true;
            }

            _view!.Set(_items, _offset, count);
            _offset += count;
            chunk = _view;
            return true;
        }

        _buffer!.Clear();
        while (_buffer.Count < _maxBatchSize)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (!_enumerator!.MoveNext()) break;
            _buffer.Add(_enumerator.Current);
        }
        chunk = _buffer;
        return _buffer.Count != 0;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _enumerator?.Dispose();
    }
}

internal sealed class InquiryBatchChunkView<T> : IReadOnlyList<T>
{
    private IReadOnlyList<T>? _items;
    private int _offset;

    public int Count { get; private set; }

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            return _items![_offset + index];
        }
    }

    internal void Set(IReadOnlyList<T> items, int offset, int count)
    {
        _items = items;
        _offset = offset;
        Count = count;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (var i = 0; i < Count; i++) yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
