using System.Data.Common;

namespace Inquiry;

/// <summary>
/// Materializes mapped Inquiry entities from a data reader.
/// </summary>
public interface IInquiryEntityMaterializer<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Materializes the current reader row.
    /// </summary>
    TEntity Materialize(DbDataReader reader);
}
