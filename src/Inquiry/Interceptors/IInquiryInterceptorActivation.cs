namespace Inquiry.Interceptors;

/// <summary>
/// Allows a built-in interceptor to report that it currently has no observers. Custom
/// interceptors do not implement this contract and are always treated as active.
/// </summary>
internal interface IInquiryInterceptorActivation
{
    bool IsActive { get; }
}
