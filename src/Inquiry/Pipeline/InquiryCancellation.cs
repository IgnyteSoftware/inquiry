namespace Inquiry.Pipeline;

/// <summary>Normalizes provider cancellation exceptions at the pipeline boundary.</summary>
internal static class InquiryCancellation
{
    internal static bool RequiresCallerToken(
        OperationCanceledException exception,
        CancellationToken callerToken)
        => callerToken.IsCancellationRequested && exception.CancellationToken != callerToken;

    internal static OperationCanceledException AssociateWithCallerToken(
        OperationCanceledException exception,
        CancellationToken callerToken)
        => new(exception.Message, exception, callerToken);

    internal static void ThrowIfRequiresCallerToken(
        Exception exception,
        CancellationToken callerToken)
    {
        if (exception is OperationCanceledException oce && RequiresCallerToken(oce, callerToken))
            throw AssociateWithCallerToken(oce, callerToken);
    }
}
