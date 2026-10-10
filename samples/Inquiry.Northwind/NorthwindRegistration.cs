using Inquiry.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Northwind;

public static class NorthwindRegistration
{
    public static IServiceCollection AddNorthwindSampleStores(this IServiceCollection services)
        => services.AddInquiryGeneratedStores();
}
