using System.Data.Common;

namespace Inquiry.Materialization;

/// <summary>
/// Binds materializers that implement <see cref="IInquiryResultSetBinder{TEntity, TMaterializer}"/> to
/// the reader's current result set. Other materializers are returned unchanged.
/// </summary>
internal static class InquiryMaterializerBinding
{
    // A reader with no columns has no result set to map, and it returns no rows.
    public static IInquiryEntityMaterializer<T> Bind<T>(IInquiryEntityMaterializer<T> materializer, DbDataReader reader)
        where T : class
        => materializer is IInquiryResultSetBinder<T, IInquiryEntityMaterializer<T>> binder && reader.FieldCount > 0
            ? binder.Bind(reader)
            : materializer;

    // The type test is constant for each struct instantiation, so the JIT removes this call for
    // materializers that do not bind.
    public static TMaterializer Bind<T, TMaterializer>(TMaterializer materializer, DbDataReader reader)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
        => materializer is IInquiryResultSetBinder<T, TMaterializer> && reader.FieldCount > 0
            ? ((IInquiryResultSetBinder<T, TMaterializer>)materializer).Bind(reader)
            : materializer;
}
