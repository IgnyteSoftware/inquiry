namespace Inquiry;

public sealed class InquiryPipelineOptions
{
    private readonly List<InquiryMiddlewareRegistration> _middleware = new();

    public IReadOnlyList<InquiryMiddlewareRegistration> Middleware => _middleware;

    public InquiryPipelineOptions UseMiddleware<TMiddleware>()
        where TMiddleware : IInquiryMiddleware
    {
        _middleware.Add(InquiryMiddlewareRegistration.ForType(typeof(TMiddleware)));
        return this;
    }

    public InquiryPipelineOptions UseMiddleware(IInquiryMiddleware middleware)
    {
        _middleware.Add(InquiryMiddlewareRegistration.ForInstance(middleware ?? throw new ArgumentNullException(nameof(middleware))));
        return this;
    }

    public InquiryPipelineOptions UseMiddleware(Func<IServiceProvider?, IInquiryMiddleware> factory)
    {
        _middleware.Add(InquiryMiddlewareRegistration.ForFactory(factory ?? throw new ArgumentNullException(nameof(factory))));
        return this;
    }
}

public sealed class InquiryMiddlewareRegistration
{
    private readonly IInquiryMiddleware? _instance;
    private readonly Func<IServiceProvider?, IInquiryMiddleware>? _factory;

    private InquiryMiddlewareRegistration(Type? middlewareType, IInquiryMiddleware? instance, Func<IServiceProvider?, IInquiryMiddleware>? factory)
    {
        MiddlewareType = middlewareType;
        _instance = instance;
        _factory = factory;
    }

    public Type? MiddlewareType { get; }

    public IInquiryMiddleware? Instance => _instance;

    public Func<IServiceProvider?, IInquiryMiddleware>? Factory => _factory;

    public static InquiryMiddlewareRegistration ForType(Type middlewareType)
    {
        if (!typeof(IInquiryMiddleware).IsAssignableFrom(middlewareType))
        {
            throw new ArgumentException($"Type '{middlewareType.FullName}' must implement {nameof(IInquiryMiddleware)}.", nameof(middlewareType));
        }

        return new InquiryMiddlewareRegistration(middlewareType, null, null);
    }

    public static InquiryMiddlewareRegistration ForInstance(IInquiryMiddleware middleware)
    {
        return new InquiryMiddlewareRegistration(null, middleware, null);
    }

    public static InquiryMiddlewareRegistration ForFactory(Func<IServiceProvider?, IInquiryMiddleware> factory)
    {
        return new InquiryMiddlewareRegistration(null, null, factory);
    }

    public IInquiryMiddleware Create(
        IServiceProvider? services = null,
        Func<IServiceProvider, Type, IInquiryMiddleware>? typeActivator = null)
    {
        if (_instance is not null)
        {
            return _instance;
        }

        if (_factory is not null)
        {
            return _factory(services);
        }

        if (MiddlewareType is null)
        {
            throw new InquiryValidationException("Middleware registration does not define a type, instance, or factory.");
        }

        if (services is not null && typeActivator is not null)
        {
            return typeActivator(services, MiddlewareType);
        }

        return (IInquiryMiddleware)Activator.CreateInstance(MiddlewareType)!;
    }
}
