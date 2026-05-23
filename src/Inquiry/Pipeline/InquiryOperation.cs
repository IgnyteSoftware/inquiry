namespace Inquiry;

public enum InquiryOperation
{
    Unknown = 0,
    Find,
    Select,
    Insert,
    InsertMany,
    Update,
    Delete,
    Upsert,
    RawQuery,
    RawExecute,
    StoredProcedureQuery,
    StoredProcedureExecute,
    Transaction
}
