namespace Inquiry;

/// <summary>
/// Generates a method that selects all rows for the store entity.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquirySelectAttribute : Attribute
{
}

/// <summary>
/// Generates a method that selects a single row by primary key.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquirySelectByKeyAttribute : Attribute
{
}

/// <summary>
/// Generates a method that selects rows by one mapped property or column.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquirySelectByFieldAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquirySelectByFieldAttribute"/> class.
    /// </summary>
    public InquirySelectByFieldAttribute(string field)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            throw new ArgumentException("Field cannot be empty.", nameof(field));
        }

        Field = field;
    }

    /// <summary>
    /// Gets the mapped property or column name used in the generated WHERE clause.
    /// </summary>
    public string Field { get; }
}

/// <summary>
/// Generates a method that inserts an entity.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryInsertAttribute : Attribute
{
}

/// <summary>
/// Generates a method that updates an entity by primary key.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryUpdateAttribute : Attribute
{
}

/// <summary>
/// Generates a method that deletes an entity by primary key.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryDeleteByKeyAttribute : Attribute
{
}
