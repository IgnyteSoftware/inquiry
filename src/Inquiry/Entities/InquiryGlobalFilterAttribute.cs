namespace Inquiry.Entities;

/// <summary>
/// Adds a shared visibility predicate to supported generated reads. Use a boolean column for
/// static visibility, or ContextKey for a value from the current InquiryFilterContext.
/// </summary>
/// <remarks>
/// The property also needs InquiryColumn. Filters combine with AND. Named filters support
/// per-method read bypass. Raw SQL and stored procedures do not inherit these predicates.
/// Filters do not authenticate callers or replace application authorization or database policies.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InquiryGlobalFilterAttribute : Attribute
{
    /// <summary>
    /// The boolean value a static filter keeps. Defaults to true. Do not set with ContextKey.
    /// </summary>
    public bool KeepWhen { get; set; } = true;

    /// <summary>
    /// Optional name for InquiryIgnoreFilter on supported generated reads. An unnamed filter
    /// cannot be ignored through that attribute; raw SQL and procedures remain outside this mechanism.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Binds the filter value from InquiryFilterContext at execution. A missing, null, or incorrectly
    /// typed value throws InquiryFilterValueMissingException before a filtered command executes.
    /// </summary>
    /// <remarks>
    /// Parameterized filters accept non-nullable mapped scalar columns, including key components.
    /// Generated eager loading rejects parameterized filters. Inserts do not require this scope.
    /// </remarks>
    public string? ContextKey { get; set; }

    /// <summary>
    /// Adds this predicate to generated entity updates, batch updates, key deletes, restores,
    /// hard predicate deletes, and table-wide deletes. Defaults to false.
    /// </summary>
    /// <remarks>
    /// Predicate updates and soft predicate deletes already use read filters. Write enforcement
    /// checks the existing row; it does not prevent reassignment unless PreventReassignment is set.
    /// Inserts retain caller-supplied values without stamping or validation. Upsert is rejected
    /// with INQ095. InquiryIgnoreFilter cannot bypass write enforcement.
    /// Zero affected rows can mean a hidden row, a missing row, or a concurrency conflict.
    /// </remarks>
    public bool EnforceOnWrites { get; set; }

    /// <summary>
    /// Excludes this column from entity updates and rejects explicit SET assignments.
    /// Requires EnforceOnWrites. Inserts retain the caller's value without validation or stamping.
    /// </summary>
    public bool PreventReassignment { get; set; }
}
