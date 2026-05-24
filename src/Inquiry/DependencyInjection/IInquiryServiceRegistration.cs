using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.DependencyInjection;

/// <summary>
/// Registers generated Inquiry services for a consuming assembly.
/// </summary>
public interface IInquiryServiceRegistration
{
    /// <summary>
    /// Registers generated Inquiry services.
    /// </summary>
    void AddServices(IServiceCollection services);
}
