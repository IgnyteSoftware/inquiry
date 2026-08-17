using System.ComponentModel;

namespace Inquiry.BulkCopy;

/// <summary>Provider-neutral generic value writer used by generated bulk-column accessors.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IInquiryBulkValueWriter
{
    /// <summary>Writes one non-null typed value.</summary>
    ValueTask WriteAsync<T>(T value, int ordinal, CancellationToken cancellationToken);
}

/// <summary>A generated, strongly typed accessor for one bulk-insert column.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IInquiryBulkColumnAccessor<in TEntity>
    where TEntity : class
{
    /// <summary>The exact non-null CLR type returned by this accessor.</summary>
    Type FieldType { get; }

    /// <summary>The typed delegate, exposed for <see cref="System.Data.Common.DbDataReader.GetFieldValue{T}(int)"/>.</summary>
    Delegate Accessor { get; }

    /// <summary>Returns whether the entity's value is database null.</summary>
    bool IsNull(TEntity entity);

    /// <summary>Writes the non-null value through a provider writer without boxing.</summary>
    ValueTask WriteAsync<TWriter>(TEntity entity, TWriter writer, int ordinal, CancellationToken cancellationToken)
        where TWriter : IInquiryBulkValueWriter;
}

/// <summary>Default implementation used by generated stores for one typed column.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class InquiryBulkColumnAccessor<TEntity, TValue> : IInquiryBulkColumnAccessor<TEntity>
    where TEntity : class
{
    private readonly Func<TEntity, TValue> _getValue;
    private readonly Func<TEntity, bool>? _isNull;

    /// <summary>Initializes a non-nullable accessor.</summary>
    public InquiryBulkColumnAccessor(Func<TEntity, TValue> getValue)
        : this(getValue, null)
    {
    }

    /// <summary>Initializes an accessor with an optional database-null predicate.</summary>
    public InquiryBulkColumnAccessor(Func<TEntity, TValue> getValue, Func<TEntity, bool>? isNull)
    {
        _getValue = getValue ?? throw new ArgumentNullException(nameof(getValue));
        _isNull = isNull;
    }

    /// <inheritdoc />
    public Type FieldType => typeof(TValue);

    /// <inheritdoc />
    public Delegate Accessor => _getValue;

    /// <inheritdoc />
    public bool IsNull(TEntity entity) => _isNull?.Invoke(entity) ?? false;

    /// <inheritdoc />
    public ValueTask WriteAsync<TWriter>(TEntity entity, TWriter writer, int ordinal, CancellationToken cancellationToken)
        where TWriter : IInquiryBulkValueWriter
        => writer.WriteAsync(_getValue(entity), ordinal, cancellationToken);
}
