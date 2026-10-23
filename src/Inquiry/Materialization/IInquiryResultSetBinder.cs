using System.ComponentModel;
using System.Data.Common;

namespace Inquiry.Materialization;

/// <summary>
/// Binds a materializer to the columns of one result set before its first row is read.
/// </summary>
/// <remarks>
/// Generated materializers for handwritten SQL implement this capability to map result columns to
/// properties by name. The pipeline calls <see cref="Bind"/> once per result set and reads every row
/// of that result set through the returned materializer. The bound materializer carries the
/// per-reader state, so a shared singleton materializer never stores reader ordinals. Custom
/// materializers do not need this capability. The pipeline binds a class materializer only when it
/// implements this interface with <typeparamref name="TMaterializer"/> set to
/// <see cref="IInquiryEntityMaterializer{TEntity}"/>, and a struct materializer only when
/// <typeparamref name="TMaterializer"/> is the struct's own type.
/// </remarks>
/// <typeparam name="TEntity">The materialized row type.</typeparam>
/// <typeparam name="TMaterializer">The type of the bound materializer.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S3246:Generic type parameters should be co/contravariant when possible", Justification = "The pipeline matches the exact binder type for each materializer type; variance would let a struct materializer match a binder that returns a different type.")]
public interface IInquiryResultSetBinder<TEntity, TMaterializer>
    where TEntity : class
    where TMaterializer : IInquiryEntityMaterializer<TEntity>
{
    /// <summary>
    /// Returns a materializer bound to the columns of the reader's current result set.
    /// </summary>
    /// <exception cref="InvalidOperationException">The result set cannot be mapped to <typeparamref name="TEntity"/>.</exception>
    TMaterializer Bind(DbDataReader reader);
}
