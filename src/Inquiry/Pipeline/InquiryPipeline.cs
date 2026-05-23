namespace Inquiry;

internal static class InquiryPipeline
{
    public static InquiryRequestDelegate Build(
        IReadOnlyList<IInquiryMiddleware> middleware,
        InquiryRequestDelegate terminal)
    {
        var next = terminal;
        for (var index = middleware.Count - 1; index >= 0; index--)
        {
            var current = middleware[index];
            var capturedNext = next;
            next = context => current.InvokeAsync(context, capturedNext, context.CancellationToken);
        }

        return next;
    }
}
