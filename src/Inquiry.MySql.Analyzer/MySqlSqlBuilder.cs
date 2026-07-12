using Inquiry.Generators.Abstractions;

namespace Inquiry.MySql.Analyzer;

/// <summary>
/// MySQL SQL builder. Inherits shared MySQL-family SQL from <see cref="MySqlFamilySqlBuilder"/>
/// and adds the MySQL 8.0+ <c>JSON_TABLE</c> IN optimization (#169): IN collections bind as a
/// single JSON array parameter instead of per-element sentinel expansion.
/// </summary>
internal sealed class MySqlSqlBuilder : MySqlFamilySqlBuilder
{
    public override string DialectName => "MySql";

    public override CyclicForeignKeyStrategy CyclicForeignKeyStrategy => CyclicForeignKeyStrategy.AlterTable;
    public override bool SupportsCheckConstraints => true;
    public override ConstraintNameScope IndexNameScope => ConstraintNameScope.Table;
    public override IdentifierComparison IndexNameComparison => IdentifierComparison.OrdinalIgnoreCase;
    public override IdentifierComparison ForeignKeyConstraintNameComparison => IdentifierComparison.OrdinalIgnoreCase;
    public override bool SupportsReferentialAction(ReferentialActionKind action, ReferentialActionEvent @event) => action is ReferentialActionKind.NoAction or ReferentialActionKind.Restrict or ReferentialActionKind.Cascade or ReferentialActionKind.SetNull;

}
