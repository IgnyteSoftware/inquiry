namespace Inquiry.Stores;

/// <summary>
/// Marks an abstract store method as a stored procedure call.
/// Method parameters (excluding the trailing <see cref="System.Threading.CancellationToken"/>) become
/// stored procedure parameters. The return type may be <c>IAsyncEnumerable&lt;TEntity&gt;</c>,
/// <c>Task&lt;TEntity?&gt;</c>, or <c>Task&lt;int&gt;</c>.
/// </summary>
/// <remarks>
/// To surface a single value the procedure produces through an OUTPUT parameter or its RETURN
/// value, set <see cref="OutputParameter"/> or <see cref="ReturnsValue"/> and declare the method
/// as <c>Task&lt;TScalar&gt;</c> — the read-back value becomes the task result. The two are
/// mutually exclusive, and a RETURN value is always <c>Task&lt;int&gt;</c>. This scalar-output form
/// does not also map a result set; use a separate method for the rows.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InquiryStoredProcedureAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of <see cref="InquiryStoredProcedureAttribute"/> with the procedure name.
    /// </summary>
    public InquiryStoredProcedureAttribute(string procedureName)
    {
        if (string.IsNullOrWhiteSpace(procedureName))
        {
            throw new ArgumentException("Procedure name cannot be empty.", nameof(procedureName));
        }

        ProcedureName = procedureName;
    }

    /// <summary>
    /// Gets the stored procedure name to execute.
    /// </summary>
    public string ProcedureName { get; }

    /// <summary>
    /// Names an <c>OUTPUT</c> parameter whose post-execution value is returned as the method's
    /// <c>Task&lt;TScalar&gt;</c> result (the value's type is the method's <c>TScalar</c>). The name
    /// may be given with or without a leading <c>@</c>. Mutually exclusive with
    /// <see cref="ReturnsValue"/>.
    /// </summary>
    public string? OutputParameter { get; set; }

    /// <summary>
    /// When <see langword="true"/>, the procedure's integer <c>RETURN</c> value is returned as the
    /// method's result — the method must be declared <c>Task&lt;int&gt;</c>. Mutually exclusive with
    /// <see cref="OutputParameter"/>.
    /// </summary>
    public bool ReturnsValue { get; set; }
}
