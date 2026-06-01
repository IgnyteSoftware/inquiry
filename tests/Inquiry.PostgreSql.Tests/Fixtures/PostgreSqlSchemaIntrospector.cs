using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.IntegrationTesting;

namespace Inquiry.PostgreSql.Tests.Fixtures;

public sealed class PostgreSqlSchemaIntrospector : ISchemaIntrospector
{
    public async Task<SchemaSnapshot> ReadAsync(DbConnection conn, CancellationToken ct = default)
    {
        var cols = new Dictionary<string, List<ColumnSnapshot>>();
        await Query(conn, ct,
            @"SELECT table_name, column_name, is_nullable
              FROM information_schema.columns
              WHERE table_schema = 'public' ORDER BY table_name, ordinal_position;",
            r =>
            {
                var t = r.GetString(0);
                if (!cols.TryGetValue(t, out var list)) cols[t] = list = new();
                list.Add(new ColumnSnapshot(r.GetString(1), r.GetString(2) == "YES"));
            });

        var pks = new Dictionary<string, List<string>>();
        await Query(conn, ct,
            @"SELECT tc.table_name, kcu.column_name
              FROM information_schema.table_constraints tc
              JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
              WHERE tc.table_schema='public' AND tc.constraint_type='PRIMARY KEY'
              ORDER BY tc.table_name, kcu.ordinal_position;",
            r => { var t = r.GetString(0); (pks.TryGetValue(t, out var l) ? l : pks[t] = new()).Add(r.GetString(1)); });

        var fks = new Dictionary<string, List<ForeignKeySnapshot>>();
        await Query(conn, ct,
            @"SELECT tc.table_name, kcu.column_name, ccu.table_name AS ref_table, ccu.column_name AS ref_col
              FROM information_schema.table_constraints tc
              JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
              JOIN information_schema.constraint_column_usage ccu
                ON ccu.constraint_name = tc.constraint_name AND ccu.table_schema = tc.table_schema
              WHERE tc.table_schema='public' AND tc.constraint_type='FOREIGN KEY';",
            r =>
            {
                var t = r.GetString(0);
                (fks.TryGetValue(t, out var l) ? l : fks[t] = new())
                    .Add(new ForeignKeySnapshot(new[] { r.GetString(1) }, r.GetString(2), new[] { r.GetString(3) }));
            });

        var idx = new Dictionary<string, List<IndexSnapshot>>();
        await Query(conn, ct,
            @"SELECT t.relname AS table_name,
                     array_to_string(array_agg(a.attname ORDER BY k.ord), ',') AS cols
              FROM pg_index ix
              JOIN pg_class i ON i.oid = ix.indexrelid
              JOIN pg_class t ON t.oid = ix.indrelid
              JOIN pg_namespace n ON n.oid = t.relnamespace
              JOIN unnest(ix.indkey) WITH ORDINALITY AS k(attnum, ord) ON true
              JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum
              WHERE n.nspname='public'
              GROUP BY i.relname, t.relname;",
            r =>
            {
                var t = r.GetString(0);
                var c = r.GetString(1).Split(',');
                (idx.TryGetValue(t, out var l) ? l : idx[t] = new()).Add(new IndexSnapshot(c));
            });

        var tables = cols.Keys.Select(t => new TableSnapshot(
            t, cols[t],
            pks.TryGetValue(t, out var p) ? p : new List<string>(),
            fks.TryGetValue(t, out var f) ? f : new List<ForeignKeySnapshot>(),
            idx.TryGetValue(t, out var ii) ? ii : new List<IndexSnapshot>())).ToList();

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
