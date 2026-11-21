namespace Inquiry.Stores;

/// <summary>
/// How a <see cref="Compare.In"/> criterion sends its collection to the database. Set it per criterion
/// with <see cref="InquiryWhereAttribute.InListBinding"/>. Only SQL Server has a choice; the other
/// providers already send lists as arrays or JSON without database objects, and ignore this setting.
/// </summary>
public enum InListBinding
{
    /// <summary>
    /// Use the provider default. On SQL Server that is a generated table type, unless the assembly applies
    /// <c>[assembly: Inquiry.SqlServer.SqlServerInListBinding(InListBinding.Json)]</c>.
    /// </summary>
    Default,

    /// <summary>
    /// SQL Server: bind the list as one generated table-valued parameter. The <c>Inquiry_Tvp_*</c> table
    /// type must exist in the database.
    /// </summary>
    TableValuedParameter,

    /// <summary>
    /// SQL Server: send the list as one JSON array parameter and expand it with <c>OPENJSON ... WITH</c>,
    /// typed as the column's SQL type. No table type is needed. Requires database compatibility level 130
    /// or later.
    /// </summary>
    Json,
}
