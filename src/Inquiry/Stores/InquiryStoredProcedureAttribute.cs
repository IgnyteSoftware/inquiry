namespace Inquiry.Stores;

/// <summary>
/// Marks an abstract store method as a stored procedure call.
/// Method parameters (excluding the trailing <see cref="System.Threading.CancellationToken"/>) become
/// stored procedure parameters. The return type may be <c>IAsyncEnumerable&lt;TEntity&gt;</c>,
/// <c>Task&lt;TEntity?&gt;</c>, or <c>Task&lt;int&gt;</c>.
/// </summary>
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
}
