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
    Count,
    Aggregate,
    FullTextSearch,
    InsertAll,
    Insert,
    Update,
    Upsert,
    DeleteOneByKey,
    RestoreOneByKey,
    StoredProcedure,
}
