using Inquiry.Generators;
using Inquiry.Generators.Abstractions;
using Microsoft.CodeAnalysis;

namespace Inquiry.Sqlite.Analyzer;

/// <summary>
/// The Inquiry source generator instance that emits SQLite-flavoured store SQL. Roslyn loads this
/// analyzer assembly when the consumer references <c>Inquiry.Sqlite</c>; the base class handles
/// candidate discovery, dialect arbitration (in case multiple providers are referenced), and
/// emission, calling back into <see cref="CreateSqlBuilder"/> for the dialect-specific SQL.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class InquirySqliteGenerator : InquiryGeneratorBase
{
    protected override string Dialect => "Sqlite";

    protected override SqlBuilder CreateSqlBuilder() => new SqliteSqlBuilder();
}
