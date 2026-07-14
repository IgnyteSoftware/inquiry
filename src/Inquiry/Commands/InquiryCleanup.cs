using System.Runtime.ExceptionServices;

namespace Inquiry.Commands;

internal static class InquiryCleanup
{
    internal static List<Exception> Add(List<Exception>? exceptions, Exception exception)
    {
        exceptions ??= new List<Exception>();
        exceptions.Add(exception);
        return exceptions;
    }

    internal static void ThrowIfAny(List<Exception>? exceptions)
    {
        if (exceptions is null || exceptions.Count == 0) return;
        if (exceptions.Count == 1) ExceptionDispatchInfo.Capture(exceptions[0]).Throw();
        throw new AggregateException("Multiple failures occurred while releasing Inquiry execution resources.", exceptions);
    }

    internal static void ThrowIfCleanupFailed(Exception primaryException, List<Exception>? cleanupExceptions)
    {
        if (cleanupExceptions is null || cleanupExceptions.Count == 0) return;
        var exceptions = new List<Exception>(cleanupExceptions.Count + 1) { primaryException };
        exceptions.AddRange(cleanupExceptions);
        throw new AggregateException("Inquiry execution failed and one or more resources also failed to release.", exceptions);
    }

}
