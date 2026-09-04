namespace Inquiry.Stores;

/// <summary>
/// Generates an explicit table-wide delete. The method takes only a
/// <see cref="System.Threading.CancellationToken"/> and returns the number of affected rows.
/// </summary>
/// <remarks>
/// For a soft-delete entity, the default form marks every active row as deleted. Set
/// <see cref="HardDelete"/> to emit a literal <c>DELETE</c>. Global filters continue to enforce their
/// configured write behavior.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryDeleteAllAttribute : Attribute
{
    /// <summary>
    /// Gets or sets a value indicating whether the method emits a literal <c>DELETE</c> for an entity
    /// with an <c>[InquirySoftDelete]</c> column. The default form updates the soft-delete indicator.
    /// </summary>
    public bool HardDelete { get; set; }
}
