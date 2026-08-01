using Inquiry.Generators;
using Inquiry.Generators.Abstractions;
using Microsoft.CodeAnalysis;

namespace Inquiry.MariaDb.Analyzer;

/// <summary>
/// The Inquiry source generator instance that emits MariaDB-flavoured store SQL. Roslyn loads
/// this analyzer assembly when the consumer references <c>Inquiry.MariaDb</c>; the base class handles
/// candidate discovery, dialect arbitration, and emission, calling back into
/// <see cref="CreateSqlBuilder"/> for the dialect-specific SQL.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class InquiryMariaDbGenerator : InquiryGeneratorBase
{
    protected override string Dialect => "MariaDb";

    protected override SqlBuilder CreateSqlBuilder() => new MariaDbSqlBuilder();
}
