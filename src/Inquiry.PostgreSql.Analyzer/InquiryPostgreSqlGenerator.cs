using Inquiry.Generators;
using Inquiry.Generators.Abstractions;
using Microsoft.CodeAnalysis;

namespace Inquiry.PostgreSql.Analyzer;

/// <summary>
/// The Inquiry source generator instance that emits PostgreSQL-flavoured store SQL. Roslyn loads
/// this analyzer assembly when the consumer references <c>Inquiry.PostgreSql</c>; the base class
/// handles candidate discovery, dialect arbitration, and emission, calling back into
/// <see cref="CreateSqlBuilder"/> for the dialect-specific SQL.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class InquiryPostgreSqlGenerator : InquiryGeneratorBase
{
    protected override string Dialect => "PostgreSql";

    protected override SqlBuilder CreateSqlBuilder() => new PostgreSqlSqlBuilder();
}
