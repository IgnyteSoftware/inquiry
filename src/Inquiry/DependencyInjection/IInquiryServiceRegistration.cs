using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;

namespace Inquiry.DependencyInjection;

/// <summary>
/// Registers generated Inquiry services for a consuming assembly.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IInquiryServiceRegistration
{
    /// <summary>
    /// Registers generated Inquiry services.
    /// </summary>
    void AddServices(IServiceCollection services);
}
