namespace Inquiry.Entities;

/// <summary>
/// Sets the <see cref="System.DateTimeKind"/> that generated materializers give <see cref="System.DateTime"/>
/// values read from the database. Most providers return <see cref="System.DateTimeKind.Unspecified"/>
/// for timestamp-without-time-zone types, such as SQL Server <c>datetime2</c>.
/// </summary>
/// <remarks>
/// <para>
/// Apply it to the assembly to set the default for every <see cref="System.DateTime"/> column, for example
/// <c>[assembly: InquiryDateTimeKind(DateTimeKind.Utc)]</c>. Apply it to a property of an entity,
/// <see cref="InquiryProjectionAttribute"/> type or <see cref="InquiryAdHocAttribute"/> DTO to override
/// that default for one column. Without either, values keep the kind the provider returns.
/// </para>
/// <para>
/// The generated read calls <see cref="System.DateTime.SpecifyKind(System.DateTime, System.DateTimeKind)"/>,
/// so the ticks do not change. Writes are not changed. When the column has a value converter whose
/// provider type is <see cref="System.DateTime"/>, the kind is set on the value the converter receives.
/// On a property, the attribute is valid only where the value read from the database is a
/// <see cref="System.DateTime"/>. Any other use is build error <c>INQ119</c>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InquiryDateTimeKindAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="InquiryDateTimeKindAttribute"/> class.</summary>
    /// <param name="kind">The kind that read values get.</param>
    public InquiryDateTimeKindAttribute(DateTimeKind kind)
    {
        Kind = kind;
    }

    /// <summary>Gets the kind that read values get.</summary>
    public DateTimeKind Kind { get; }
}
