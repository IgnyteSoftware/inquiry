using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.IntegrationTesting;

namespace Inquiry.Oracle.Tests.Fixtures;

/// <summary>Reads the throwaway schema's catalog through the <c>USER_*</c> data-dictionary views (the
/// schema user owns every object). Oracle folds unquoted identifiers to UPPERCASE, which is fine —
/// <see cref="SchemaFidelity"/> matches case-insensitively; <c>"Order Details"</c> is quoted in the DDL
/// so it comes back with its original mixed case.</summary>
public sealed class OracleSchemaIntrospector : ISchemaIntrospector
{
    public async Task<SchemaSnapshot> ReadAsync(DbConnection conn, CancellationToken ct = default)
    {
        var cols = new Dictionary<string, List<ColumnSnapshot>>();
        await Query(conn, ct,
            @"SELECT table_name, column_name, nullable
              FROM user_tab_columns
              ORDER BY table_name, column_id",
            r =>
            {
                var t = r.GetString(0);
                if (!cols.TryGetValue(t, out var list)) cols[t] = list = new();
                list.Add(new ColumnSnapshot(r.GetString(1), r.GetString(2) == "Y"));
            });

        var pks = new Dictionary<string, List<string>>();
        await Query(conn, ct,
            @"SELECT c.table_name, cc.column_name
              FROM user_constraints c
              JOIN user_cons_columns cc ON cc.constraint_name = c.constraint_name
              WHERE c.constraint_type = 'P'
              ORDER BY c.table_name, cc.position",
            r => { var t = r.GetString(0); (pks.TryGetValue(t, out var l) ? l : pks[t] = new()).Add(r.GetString(1)); });

        var fks = new Dictionary<string, List<ForeignKeySnapshot>>();
        await Query(conn, ct,
            @"SELECT c.table_name, cc.column_name, rc.table_name AS ref_table, rcc.column_name AS ref_col
              FROM user_constraints c
              JOIN user_cons_columns cc  ON cc.constraint_name = c.constraint_name
              JOIN user_constraints rc   ON rc.constraint_name = c.r_constraint_name
              JOIN user_cons_columns rcc ON rcc.constraint_name = rc.constraint_name
                                        AND rcc.position = cc.position
              WHERE c.constraint_type = 'R'
              ORDER BY c.table_name, c.constraint_name, cc.position",
            r =>
            {
                var t = r.GetString(0);
                (fks.TryGetValue(t, out var l) ? l : fks[t] = new())
                    .Add(new ForeignKeySnapshot(new[] { r.GetString(1) }, r.GetString(2), new[] { r.GetString(3) }));
            });

        var idx = new Dictionary<(string Table, string Index), List<string>>();
        await Query(conn, ct,
            @"SELECT table_name, index_name, column_name
              FROM user_ind_columns
              ORDER BY table_name, index_name, column_position",
            r =>
            {
                var key = (r.GetString(0), r.GetString(1));
                (idx.TryGetValue(key, out var l) ? l : idx[key] = new()).Add(r.GetString(2));
            });

        var idxByTable = idx
            .GroupBy(kv => kv.Key.Table)
            .ToDictionary(g => g.Key, g => g.Select(kv => new IndexSnapshot(kv.Value)).ToList());

        var tables = cols.Keys.Select(t => new TableSnapshot(
            t, cols[t],
            pks.TryGetValue(t, out var p) ? p : new List<string>(),
            fks.TryGetValue(t, out var f) ? f : new List<ForeignKeySnapshot>(),
            idxByTable.TryGetValue(t, out var ii) ? ii : new List<IndexSnapshot>())).ToList();

        return new SchemaSnapshot(tables);
    }

    private static async Task Query(DbConnection conn, CancellationToken ct, string sql, Action<DbDataReader> onRow)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) onRow(r);
    }
}
