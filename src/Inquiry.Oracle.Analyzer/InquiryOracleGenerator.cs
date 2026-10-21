using Inquiry.Generators;
using Inquiry.Generators.Abstractions;
using Microsoft.CodeAnalysis;

namespace Inquiry.Oracle.Analyzer;

/// <summary>
/// The Inquiry source generator instance that emits Oracle-flavoured store SQL. Roslyn loads this
/// analyzer assembly when the consumer references <c>Inquiry.Oracle</c>; the base class handles
/// candidate discovery, dialect arbitration, and emission, calling back into
/// <see cref="CreateSqlBuilder"/> for the dialect-specific SQL.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class InquiryOracleGenerator : InquiryGeneratorBase
{
    protected override string Dialect => "Oracle";

    protected override SqlBuilder CreateSqlBuilder() => new OracleSqlBuilder();
}
