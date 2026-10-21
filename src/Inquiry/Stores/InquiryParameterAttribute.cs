namespace Inquiry.Stores;

/// <summary>
/// Annotates a stored-procedure method parameter with provider-aware metadata so the generator
/// emits explicit <c>DbType</c>, <c>Size</c>, <c>Precision</c>, and <c>Scale</c> on the
/// generated <see cref="System.Data.Common.DbParameter"/>. Without this attribute the generator
/// still emits <c>DbType</c> inferred from the CLR type; the attribute overrides or supplements
/// that default with the values needed for stable parameter signatures and correct binding.
/// </summary>
/// <remarks>
/// <para>
/// On SQL Server, explicit <c>Size</c> on string/binary parameters keeps the
/// <c>sp_executesql</c> parameter signature stable across value lengths, preventing plan-cache
/// bloat. <c>Precision</c>/<c>Scale</c> on decimal parameters ensures the server-side type
/// matches the procedure's formal parameter and avoids silent rounding.
/// </para>
/// <para>
/// This attribute is only meaningful on parameters of methods decorated with
/// <see cref="InquiryStoredProcedureAttribute"/>. It is silently ignored on parameters of
/// other store methods whose parameter metadata is derived from the entity column.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class InquiryParameterAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the declared length for string and binary parameters. When set on a
    /// SQL Server target, the generated parameter carries <c>Size</c> so the parameterised query
    /// signature stays stable. A value of <c>0</c> (the default) means the length is not declared
    /// and the provider infers the size from the value.
    /// </summary>
    public int Length { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a string parameter uses a Unicode type
    /// (<c>DbType.String</c> / <c>nvarchar</c>) or an ANSI type (<c>DbType.AnsiString</c> /
    /// <c>varchar</c>). The default is <see langword="true"/> (Unicode). Has no effect on
    /// non-string parameters.
    /// </summary>
    public bool IsUnicode { get; set; } = true;

    /// <summary>
    /// Gets or sets the declared numeric precision for decimal parameters. When set, the
    /// generated parameter carries <c>Precision</c> so the server-side decimal type matches the
    /// procedure's formal parameter. A value of <c>0</c> (the default) means the precision is
    /// not declared and the provider uses its own default.
    /// </summary>
    public int Precision { get; set; }

    /// <summary>
    /// Gets or sets the declared numeric scale for decimal parameters. When set together with
    /// <see cref="Precision"/>, the generated parameter carries <c>Scale</c>. A value of <c>0</c>
    /// (the default) means the scale is not declared.
    /// </summary>
    public int Scale { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that this parameter is an input/output parameter
    /// (<c>ParameterDirection.InputOutput</c>). The caller's value is sent to the stored procedure,
    /// and the procedure's modified value is read back as the method's <c>Task&lt;T&gt;</c> result.
    /// At most one parameter per method may be marked as input/output, and it is mutually exclusive
    /// with <see cref="InquiryStoredProcedureAttribute.OutputParameter"/> and
    /// <see cref="InquiryStoredProcedureAttribute.ReturnsValue"/>.
    /// </summary>
    public bool IsInputOutput { get; set; }

    /// <summary>
    /// Gets or sets the schema-qualified SQL Server table type name for a table-valued parameter
    /// (TVP). Required on <c>IEnumerable&lt;T&gt;</c> / <c>IReadOnlyList&lt;T&gt;</c> parameters
    /// of <see cref="InquiryStoredProcedureAttribute"/> methods. The name must be bracket-quoted
    /// (<c>[dbo].[MyIdList]</c>); the type must already exist in the target database.
    /// </summary>
    public string? TvpTypeName { get; set; }
}
