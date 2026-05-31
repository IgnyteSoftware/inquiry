using Inquiry.Generators;
using Inquiry.Generators.Abstractions;
using Microsoft.CodeAnalysis;

namespace Inquiry.MySql.Analyzer;

/// <summary>
/// The Inquiry source generator instance that emits MySQL/MariaDB-flavoured store SQL. Roslyn loads
/// this analyzer assembly when the consumer references <c>Inquiry.MySql</c>; the base class handles
/// candidate discovery, dialect arbitration, and emission, calling back into
/// <see cref="CreateSqlBuilder"/> for the dialect-specific SQL.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class InquiryMySqlGenerator : InquiryGeneratorBase
{
    protected override string Dialect => "MySql";

    protected override SqlBuilder CreateSqlBuilder() => new MySqlSqlBuilder();
}
