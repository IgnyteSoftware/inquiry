using System.Data.Common;

namespace Inquiry;

/// <summary>
/// Classifies provider exceptions without replacing them. Inquiry passes provider exceptions
/// through unchanged, so these checks inspect the exception that the provider threw.
/// </summary>
public static class InquiryExceptionExtensions
{
    private static readonly Lock _gate = new();
    private static Func<DbException, bool>[] _uniqueViolationClassifiers = [];

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="exception"/> reports a unique-key or
    /// primary-key violation from an Inquiry provider. The check also finds the provider exception
    /// when it is the inner exception of an <see cref="OperationCanceledException"/>, or one of the
    /// inner exceptions of an <see cref="AggregateException"/>. Other constraint violations, such as
    /// foreign-key and check constraints, return <see langword="false"/>.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>
    /// <see langword="true"/> for a unique-key or primary-key violation; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// A provider recognizes its exceptions after its connection factory is created, which happens
    /// before Inquiry runs any command on that provider.
    /// </remarks>
    public static bool IsUniqueViolation(this Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        switch (exception)
        {
            case DbException dbException:
                return Array.Exists(Volatile.Read(ref _uniqueViolationClassifiers), classifier => classifier(dbException));

            case AggregateException aggregate:
                return aggregate.InnerExceptions.Any(IsUniqueViolation);

            case OperationCanceledException { InnerException: { } inner }:
                return inner.IsUniqueViolation();

            default:
                return false;
        }
    }

    /// <summary>
    /// Adds a provider's unique-violation check. A provider calls this before it opens a connection.
    /// Adding the same check again has no effect.
    /// </summary>
    internal static void RegisterUniqueViolationClassifier(Func<DbException, bool> classifier)
    {
        lock (_gate)
        {
            if (Array.IndexOf(_uniqueViolationClassifiers, classifier) >= 0)
            {
                return;
            }

            Volatile.Write(ref _uniqueViolationClassifiers, [.. _uniqueViolationClassifiers, classifier]);
        }
    }
}
