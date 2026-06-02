using System;
using System.Collections.Generic;
using System.Linq;

namespace Inquiry.IntegrationTesting;

public sealed class SchemaFidelityException : Exception
{
    public SchemaFidelityException(string message) : base(message) { }
}

public static class SchemaFidelity
{
    private static readonly StringComparer Id = StringComparer.OrdinalIgnoreCase;

    /// <summary>Asserts every expected table/column/PK/FK/index is present in <paramref name="actual"/>.
    /// Extra tables/indexes in actual are allowed. Throws with a full discrepancy list on mismatch.</summary>
    public static void AssertMatches(SchemaSnapshot expected, SchemaSnapshot actual)
    {
        var problems = new List<string>();
        foreach (var et in expected.Tables)
        {
            var at = actual.Tables.FirstOrDefault(t => Id.Equals(t.Name, et.Name));
            if (at is null) { problems.Add($"Missing table '{et.Name}'."); continue; }

            foreach (var ec in et.Columns)
            {
                var ac = at.Columns.FirstOrDefault(c => Id.Equals(c.Name, ec.Name));
                if (ac is null) { problems.Add($"{et.Name}: missing column '{ec.Name}'."); continue; }
                if (ac.IsNullable != ec.IsNullable)
                    problems.Add($"{et.Name}.{ec.Name}: nullability expected {ec.IsNullable}, found {ac.IsNullable}.");
            }

            if (!SameColumns(et.PrimaryKey, at.PrimaryKey))
                problems.Add($"{et.Name}: PK expected ({Join(et.PrimaryKey)}), found ({Join(at.PrimaryKey)}).");

            CheckForeignKeys(et, at, problems);

            foreach (var eix in et.Indexes)
            {
                var ok = at.Indexes.Any(aix => LeadsWith(aix.Columns, eix.Columns));
                if (!ok)
                    problems.Add($"{et.Name}: missing index on ({Join(eix.Columns)}).");
            }
        }

        if (problems.Count > 0)
            throw new SchemaFidelityException(
                "Schema fidelity check failed:" + Environment.NewLine +
                string.Join(Environment.NewLine, problems.Select(p => "  - " + p)));
    }

    /// <summary>Structural check: every expected table is present with the correct primary key and foreign
    /// keys. Use for schemas stood up from Inquiry's own generated DDL, whose faithfulness is bounded by the
    /// entity model — the entity surface may omit columns (e.g. BLOB columns) and secondary-index emission is
    /// annotation-bounded, so columns and indexes are not checked here. (Composite-key bridge tables now
    /// model their key columns as foreign keys too, so FK coverage is complete for the modeled surface.) The authoritative
    /// full-contract check — all columns, FKs, and indexes — runs via <see cref="AssertMatches"/> against the
    /// hand-written DDL actually built into the container. CRUD round-trips in the same test exercise the
    /// relationships directly.</summary>
    public static void AssertStructure(SchemaSnapshot expected, SchemaSnapshot actual)
    {
        var problems = new List<string>();
        foreach (var et in expected.Tables)
        {
            var at = actual.Tables.FirstOrDefault(t => Id.Equals(t.Name, et.Name));
            if (at is null) { problems.Add($"Missing table '{et.Name}'."); continue; }

            if (!SameColumns(et.PrimaryKey, at.PrimaryKey))
                problems.Add($"{et.Name}: PK expected ({Join(et.PrimaryKey)}), found ({Join(at.PrimaryKey)}).");

            CheckForeignKeys(et, at, problems);
        }

        if (problems.Count > 0)
            throw new SchemaFidelityException(
                "Schema structure check failed:" + Environment.NewLine +
                string.Join(Environment.NewLine, problems.Select(p => "  - " + p)));
    }

    /// <summary>Appends a problem for every expected foreign key not present in <paramref name="actual"/>.</summary>
    private static void CheckForeignKeys(TableSnapshot expected, TableSnapshot actual, List<string> problems)
    {
        foreach (var efk in expected.ForeignKeys)
        {
            var ok = actual.ForeignKeys.Any(afk =>
                SameColumns(afk.Columns, efk.Columns) &&
                Id.Equals(afk.ReferencedTable, efk.ReferencedTable) &&
                SameColumns(afk.ReferencedColumns, efk.ReferencedColumns));
            if (!ok)
                problems.Add($"{expected.Name}: missing FK ({Join(efk.Columns)}) -> {efk.ReferencedTable}({Join(efk.ReferencedColumns)}).");
        }
    }

    private static bool SameColumns(IReadOnlyList<string> a, IReadOnlyList<string> b)
        => a.Count == b.Count && a.Zip(b, (x, y) => Id.Equals(x, y)).All(eq => eq);

    /// <summary>True when actual index columns start with the expected column sequence (order-sensitive prefix).</summary>
    private static bool LeadsWith(IReadOnlyList<string> actual, IReadOnlyList<string> expected)
        => actual.Count >= expected.Count &&
           expected.Select((c, i) => Id.Equals(actual[i], c)).All(eq => eq);

    private static string Join(IReadOnlyList<string> cols) => string.Join(", ", cols);
}
