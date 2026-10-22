using System.ComponentModel;
using System.Data.Common;

namespace Inquiry.Materialization;

/// <summary>
/// Materializes mapped Inquiry entities from a data reader.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S3246:Generic type parameters should be co/contravariant when possible", Justification = "Published API; adding variance changes the public contract recorded in PublicAPI.Shipped.txt.")]
public interface IInquiryEntityMaterializer<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Gets whether this materializer reads columns only in ascending ordinal order and is therefore
    /// safe to execute with <see cref="System.Data.CommandBehavior.SequentialAccess"/>.
    /// </summary>
    /// <remarks>
    /// The default is <see langword="false"/> so arbitrary user materializers retain buffered reader
    /// behavior. Generator-owned materializers override this capability because their emitted reads
    /// are known to be monotonically ordered. A custom materializer that opts in assumes responsibility
    /// for reading every value in ordinal order and for not retaining reader-backed values beyond the row.
    /// </remarks>
    bool IsInquirySequentialAccessSafe => false;

    /// <summary>
    /// Materializes the current reader row.
    /// </summary>
    TEntity Materialize(DbDataReader reader);
}
