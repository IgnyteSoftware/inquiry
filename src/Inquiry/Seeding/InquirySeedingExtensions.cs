using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Inquiry.Seeding;

/// <summary>
/// Registration and execution for <see cref="IInquiryDataSeeder"/> implementations — the
/// formalized first-run seeding convention (EF <c>UseSeeding</c> / <c>prisma db seed</c> analog).
/// </summary>
public static class InquirySeedingExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TSeeder"/> as a scoped <see cref="IInquiryDataSeeder"/>.
    /// Multiple seeders run in registration order; registering the same seeder type twice is a
    /// no-op, so the call is safe in composable registration helpers.
    /// </summary>
    public static IServiceCollection AddInquirySeeder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSeeder>(this IServiceCollection services)
        where TSeeder : class, IInquiryDataSeeder
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IInquiryDataSeeder, TSeeder>());
        return services;
    }

    /// <summary>
    /// Creates one DI scope and runs every registered <see cref="IInquiryDataSeeder"/>
    /// sequentially, in registration order. Call once at startup, after the schema exists
    /// (e.g. after applying <c>InquiryGeneratedSchema.Ddl</c> or your migrations). No registered
    /// seeders is a no-op.
    /// </summary>
    public static async Task SeedInquiryAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        await using var scope = serviceProvider.CreateAsyncScope();
        foreach (var seeder in scope.ServiceProvider.GetServices<IInquiryDataSeeder>())
        {
            await seeder.SeedAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
