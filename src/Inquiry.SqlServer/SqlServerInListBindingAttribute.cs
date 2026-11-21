using Inquiry.Stores;

namespace Inquiry.SqlServer;

/// <summary>
/// Sets how every <see cref="Compare.In"/> criterion in this assembly sends its collection to SQL Server.
/// The source generator reads it at build time, so it changes the generated SQL and the generated
/// deployment artifacts. A criterion can override it with <see cref="InquiryWhereAttribute.InListBinding"/>.
/// </summary>
/// <remarks>
/// With <see cref="InListBinding.Json"/>, a list is sent as one <c>nvarchar(max)</c> JSON parameter and
/// expanded with <c>OPENJSON ... WITH</c>, so no <c>Inquiry_Tvp_*</c> table type is needed. Short integer
/// lists still bind as scalar parameters. The database compatibility level must be 130 or later.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class SqlServerInListBindingAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="SqlServerInListBindingAttribute"/> class.</summary>
    /// <param name="binding">
    /// The binding for <see cref="Compare.In"/> criteria that do not set their own.
    /// <see cref="InListBinding.Default"/> keeps the generated table type.
    /// </param>
    public SqlServerInListBindingAttribute(InListBinding binding)
    {
        Binding = binding;
    }

    /// <summary>Gets the binding for <see cref="Compare.In"/> criteria that do not set their own.</summary>
    public InListBinding Binding { get; }
}
