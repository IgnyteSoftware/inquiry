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
    KeysetPage,
    Insert,
    Update,
    Upsert,
    DeleteOneByKey,
    StoredProcedure,
}
