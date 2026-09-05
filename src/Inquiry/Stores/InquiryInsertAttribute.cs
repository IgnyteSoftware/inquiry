namespace Inquiry.Stores;

/// <summary>
/// Generates an insert method. A method that accepts one entity inserts one row. A method that accepts
/// an <see cref="System.Collections.Generic.IEnumerable{T}"/> of entities executes batched DML in the
/// current transaction and returns the total rows affected.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryInsertAttribute : Attribute
{
    /// <summary>
    /// Gets or sets a value indicating whether the generated method returns the row produced by the database.
    /// </summary>
    public bool ReturnEntity { get; set; }
}
