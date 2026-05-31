namespace Inquiry.Generators.Models;

internal enum StoreOperation
{
    None,
    SelectAll,
    SelectAllEager,
    SelectOneByKey,
    SelectOneByKeyEager,
    SelectAllByField,
    SelectAllByPredicate,
    Insert,
    Update,
    Upsert,
    DeleteOneByKey,
    StoredProcedure,
}
