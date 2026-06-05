using System.ComponentModel;
using System.Data.Common;

namespace Inquiry.Materialization;

/// <summary>
/// Materializes mapped Inquiry entities from a data reader.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IInquiryEntityMaterializer<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Materializes the current reader row.
    /// </summary>
    TEntity Materialize(DbDataReader reader);
}
