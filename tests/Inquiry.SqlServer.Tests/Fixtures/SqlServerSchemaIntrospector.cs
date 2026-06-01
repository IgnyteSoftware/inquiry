using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.IntegrationTesting;

namespace Inquiry.SqlServer.Tests.Fixtures;

public sealed class SqlServerSchemaIntrospector : ISchemaIntrospector
{
    public async Task<SchemaSnapshot> ReadAsync(DbConnection conn, CancellationToken ct = default)
    {
        var cols = new Dictionary<string, List<ColumnSnapshot>>();
        await Query(conn, ct,
            @"SELECT t.name, c.name, c.is_nullable
              FROM sys.tables t
              JOIN sys.columns c ON c.object_id = t.object_id
              ORDER BY t.name, c.column_id;",
            r =>
            {
                var t = r.GetString(0);
                if (!cols.TryGetValue(t, out var list)) cols[t] = list = new();
                list.Add(new ColumnSnapshot(r.GetString(1), r.GetBoolean(2)));
            });

        var pks = new Dictionary<string, List<string>>();
        await Query(conn, ct,
            @"SELECT t.name, c.name
              FROM sys.key_constraints kc
              JOIN sys.tables t ON t.object_id = kc.parent_object_id
              JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
              JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE kc.type = 'PK'
              ORDER BY t.name, ic.key_ordinal;",
            r => { var t = r.GetString(0); (pks.TryGetValue(t, out var l) ? l : pks[t] = new()).Add(r.GetString(1)); });

        var fks = new Dictionary<string, List<ForeignKeySnapshot>>();
        await Query(conn, ct,
            @"SELECT t.name, pc.name, rt.name, rc.name
              FROM sys.foreign_keys fk
              JOIN sys.tables t ON t.object_id = fk.parent_object_id
              JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
              JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
              JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
              JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
              ORDER BY t.name, fk.name, fkc.constraint_column_id;",
            r =>
            {
                var t = r.GetString(0);
                (fks.TryGetValue(t, out var l) ? l : fks[t] = new())
                    .Add(new ForeignKeySnapshot(new[] { r.GetString(1) }, r.GetString(2), new[] { r.GetString(3) }));
            });

        var idxAcc = new Dictionary<(string Table, int IndexId), List<string>>();
        await Query(conn, ct,
            @"SELECT t.name, i.index_id, c.name
              FROM sys.indexes i
              JOIN sys.tables t ON t.object_id = i.object_id
              JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
              JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE i.type > 0 AND ic.is_included_column = 0
              ORDER BY t.name, i.index_id, ic.key_ordinal;",
            r =>
            {
                var key = (r.GetString(0), r.GetInt32(1));
                if (!idxAcc.TryGetValue(key, out var l)) idxAcc[key] = l = new();
                l.Add(r.GetString(2));
            });

        var idx = new Dictionary<string, List<IndexSnapshot>>();
        foreach (var ((table, _), columns) in idxAcc)
            (idx.TryGetValue(table, out var l) ? l : idx[table] = new()).Add(new IndexSnapshot(columns));

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
