using System.Data.Common;
using Inquiry.IntegrationTesting;

namespace Inquiry.Oracle.Tests.Fixtures;

/// <summary>Reads the throwaway schema's catalog through the <c>USER_*</c> data-dictionary views (the
/// schema user owns every object). Oracle folds unquoted identifiers to UPPERCASE, which is fine —
/// <see cref="SchemaFidelity"/> matches case-insensitively; <c>"Order Details"</c> is quoted in the DDL
/// so it comes back with its original mixed case.</summary>
public sealed class OracleSchemaIntrospector : ISchemaIntrospector
{
    public async Task<SchemaSnapshot> ReadAsync(DbConnection connection, CancellationToken cancellationToken = default)
    {
        var cols = new Dictionary<string, List<ColumnSnapshot>>();
        await Query(connection,
            @"SELECT table_name, column_name, nullable
              FROM user_tab_columns
              ORDER BY table_name, column_id",
            r =>
            {
                var t = r.GetString(0);
                if (!cols.TryGetValue(t, out var list))
                {
                    cols[t] = list = new();
                }

                list.Add(new ColumnSnapshot(r.GetString(1), r.GetString(2) == "Y"));
            }, cancellationToken);

        var pks = new Dictionary<string, List<string>>();
        await Query(connection,
            @"SELECT c.table_name, cc.column_name
              FROM user_constraints c
              JOIN user_cons_columns cc ON cc.constraint_name = c.constraint_name
              WHERE c.constraint_type = 'P'
              ORDER BY c.table_name, cc.position",
            r => { var t = r.GetString(0); GetOrAdd(pks, t).Add(r.GetString(1)); }, cancellationToken);

        var fks = new Dictionary<string, List<ForeignKeySnapshot>>();
        await Query(connection,
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
                GetOrAdd(fks, t)
                    .Add(new ForeignKeySnapshot(new[] { r.GetString(1) }, r.GetString(2), new[] { r.GetString(3) }));
            }, cancellationToken);

        var idx = new Dictionary<(string Table, string Index), List<string>>();
        await Query(connection,
            @"SELECT table_name, index_name, column_name
              FROM user_ind_columns
              ORDER BY table_name, index_name, column_position",
            r =>
            {
                var key = (r.GetString(0), r.GetString(1));
                GetOrAdd(idx, key).Add(r.GetString(2));
            }, cancellationToken);

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

    private static async Task Query(DbConnection connection, string sql, Action<DbDataReader> onRow, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            onRow(r);
        }
    }

    private static List<TValue> GetOrAdd<TKey, TValue>(Dictionary<TKey, List<TValue>> map, TKey key)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = new();
            map[key] = list;
        }

        return list;
    }
}
