using Inquiry.Generators;
using Inquiry.Generators.Abstractions;
using Microsoft.CodeAnalysis;

namespace Inquiry.SqlServer.Analyzer;

/// <summary>
/// The Inquiry source generator instance that emits SQL Server-flavoured store SQL. Roslyn loads
/// this analyzer assembly when the consumer references <c>Inquiry.SqlServer</c>; the base class
/// handles candidate discovery, dialect arbitration, and emission, calling back into
/// <see cref="CreateSqlBuilder"/> for the dialect-specific SQL.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class InquirySqlServerGenerator : InquiryGeneratorBase
{
    protected override string Dialect => "SqlServer";

    protected override SqlBuilder CreateSqlBuilder() => new SqlServerSqlBuilder();
}
