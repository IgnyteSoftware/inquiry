namespace Inquiry.Seeding;

/// <summary>
/// A startup data-seeding hook. Implementations are registered with
/// <c>AddInquirySeeder&lt;TSeeder&gt;()</c> and run — in registration order, inside one DI scope —
/// by <c>IServiceProvider.SeedInquiryAsync()</c>. Seeders are resolved as scoped services, so they
/// constructor-inject generated stores (or <see cref="IInquiry"/>) like any other scoped component.
/// </summary>
/// <remarks>
/// Idempotency is the seeder's responsibility — the conventional guard is "return early when the
/// table already has rows". Inquiry never runs seeders implicitly; the host decides when
/// (typically once at startup, after schema creation).
/// </remarks>
public interface IInquiryDataSeeder
{
    /// <summary>Seeds data. Called once per <c>SeedInquiryAsync()</c> invocation.</summary>
    Task SeedAsync(CancellationToken cancellationToken = default);
}
