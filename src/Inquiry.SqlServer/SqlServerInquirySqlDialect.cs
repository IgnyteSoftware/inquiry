using Inquiry;

namespace Inquiry.SqlServer;

/// <summary>
/// Provides SQL Server SQL naming and quoting behavior for Inquiry generated statements.
/// </summary>
public sealed class SqlServerInquirySqlDialect : InquirySqlDialect
{
    /// <inheritdoc />
    public override string Name => "SqlServer";

    /// <inheritdoc />
    public override string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("Identifier cannot be empty.", nameof(identifier));
        }

        return "[" + identifier.Replace("]", "]]") + "]";
    }
}
