using Inquiry.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Sample.Data;

public static class WorkspaceRegistration
{
    public static IServiceCollection AddProductWorkspace(this IServiceCollection services)
    {
        services.AddInquiryGeneratedStores();
        services.AddScoped<ProductWorkspace>();
        return services;
    }

    public static string Schema => Inquiry.Generated.InquiryGeneratedSchema.Ddl;
}
