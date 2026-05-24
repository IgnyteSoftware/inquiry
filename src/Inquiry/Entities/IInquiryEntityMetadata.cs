using Inquiry.Sql;

namespace Inquiry.Entities;

/// <summary>
/// Describes generated mapping metadata for an Inquiry entity.
/// </summary>
public interface IInquiryEntityMetadata<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Gets the mapped table name.
    /// </summary>
    string TableName { get; }

    /// <summary>
    /// Gets the mapped schema name, or <see langword="null"/> when no schema is mapped.
    /// </summary>
    string? Schema { get; }

    /// <summary>
    /// Gets the mapped columns.
    /// </summary>
    IReadOnlyList<InquirySqlColumn> Columns { get; }

    /// <summary>
    /// Gets the mapped key column.
    /// </summary>
    InquirySqlColumn Key { get; }

    /// <summary>
    /// Gets the mapped foreign keys.
    /// </summary>
    IReadOnlyList<InquiryForeignKey> ForeignKeys { get; }
}
