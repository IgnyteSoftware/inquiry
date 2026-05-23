using System.Data.Common;

namespace Inquiry;

public sealed class InquiryRequestContext
{
    public InquiryRequestContext(
        InquiryOperation operation,
        Type? entityType,
        DbConnection connection,
        DbTransaction? transaction,
        string? commandText,
        IServiceProvider? services,
        CancellationToken cancellationToken)
    {
        Operation = operation;
        EntityType = entityType;
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Transaction = transaction;
        CommandText = commandText;
        Services = services;
        CancellationToken = cancellationToken;
    }

    public InquiryOperation Operation { get; }

    public Type? EntityType { get; }

    public DbConnection Connection { get; }

    public DbTransaction? Transaction { get; }

    public string? CommandText { get; set; }

    public Dictionary<string, object?> Parameters { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IServiceProvider? Services { get; }

    public CancellationToken CancellationToken { get; }

    public Dictionary<string, object?> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class InquiryResponse
{
    public object? Result { get; init; }

    public int? RowsAffected { get; init; }

    public TimeSpan Elapsed { get; init; }
}

public delegate ValueTask<InquiryResponse> InquiryRequestDelegate(InquiryRequestContext context);

public interface IInquiryMiddleware
{
    ValueTask<InquiryResponse> InvokeAsync(
        InquiryRequestContext context,
        InquiryRequestDelegate next,
        CancellationToken cancellationToken);
}
