namespace Inquiry.Entities;

/// <summary>
/// Marks a mapped <see cref="bool"/> column as a global query filter: every generated SELECT (including
/// COUNT, aggregates, paged and keyset reads) auto-AND-composes <c>"&lt;column&gt;" = &lt;KeepWhen&gt;</c>,
/// so rows that don't match are invisible to reads without each method restating the predicate. This is
/// the EF <c>HasQueryFilter</c> parity for a static column predicate — the common shape behind
/// multi-tenant isolation (<c>TenantActive</c>), active-record filtering (<c>IsActive</c>), and
/// publish gates (<c>IsPublished</c>).
/// </summary>
/// <remarks>
/// This is an orthogonal marker — the property still needs <c>[InquiryColumn]</c>. The column must be a
/// non-nullable <see cref="bool"/> and must not double as the key, a generated/database-default column,
/// the soft-delete indicator, or a concurrency token (INQ059). (Auditing columns are timestamps/strings,
/// so the bool requirement already precludes them.) An entity may declare several global-filter columns;
/// they are AND-composed. Unlike the soft-delete filter, a global filter has no per-method opt-out — it
/// always applies — so it cannot be silently bypassed; reach for an ad-hoc query when an unfiltered read
/// is genuinely needed.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InquiryGlobalFilterAttribute : Attribute
{
    /// <summary>
    /// The <see cref="bool"/> value a row's filter column must equal to remain visible. Default
    /// <c>true</c> — keep rows where the column is true (e.g. <c>IsActive</c>). Set <c>false</c> to keep
    /// rows where the column is false (e.g. an <c>IsArchived</c> flag where the unarchived rows are kept).
    /// </summary>
    public bool KeepWhen { get; set; } = true;

    /// <summary>
    /// Optional name that makes this filter selectively bypassable: a store method annotated
    /// <c>[InquiryIgnoreFilter("name")]</c> is generated without this filter's predicate. An UNNAMED
    /// filter cannot be bypassed at all — leave <see cref="Name"/> unset for security boundaries
    /// (tenant flags) and name only filters that legitimate callers sometimes need to see through
    /// (a <c>PublishGate</c> an admin view lists drafts past). The name is resolved entirely at
    /// generation time; a method naming a filter that does not exist on the entity is a build error.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Switches the filter from a constant predicate to a RUNTIME-parameterized one: instead of
    /// <c>"col" = KeepWhen</c>, every generated SELECT composes <c>"col" = @__gf_&lt;property&gt;</c>
    /// and binds the value from the ambient <see cref="InquiryFilterContext"/> under this key at
    /// execute time — the multi-tenant shape (<c>ContextKey = "TenantId"</c> on a tenant column).
    /// The SQL is still a compile-time const; only the value is runtime, and a missing ambient value
    /// throws <see cref="InquiryFilterValueMissingException"/> before the command runs. In this mode
    /// the column may be any non-nullable mapped scalar (including a key component) rather than a
    /// bool, and <see cref="KeepWhen"/> must not be set (the modes conflict — INQ093). Identity
    /// stays separate from <see cref="Name"/>: a tenant boundary should set <see cref="ContextKey"/>
    /// and leave <see cref="Name"/> unset so it cannot be bypassed.
    /// </summary>
    public string? ContextKey { get; set; }
}
