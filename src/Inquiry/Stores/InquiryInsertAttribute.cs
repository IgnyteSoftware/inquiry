namespace Inquiry.Stores;

/// <summary>
/// Generates an insert method. A method that accepts one entity inserts one row. A method that accepts
/// an <see cref="System.Collections.Generic.IEnumerable{T}"/> of entities executes batched DML in the
/// current transaction and returns the total rows affected. A single-entity method returns either the
/// affected-row count or the inserted entity; the return type selects the generated command shape.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryInsertAttribute : Attribute
{
}
