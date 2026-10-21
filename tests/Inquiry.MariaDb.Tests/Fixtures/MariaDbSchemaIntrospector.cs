using System.Data.Common;
using System.Globalization;
using Inquiry.IntegrationTesting;

namespace Inquiry.MariaDb.Tests.Fixtures;

public sealed class MariaDbSchemaIntrospector : ISchemaIntrospector
{
    public async Task<SchemaSnapshot> ReadAsync(DbConnection connection, CancellationToken cancellationToken = default)
    {
        var cols = new Dictionary<string, List<ColumnSnapshot>>();
        await Query(connection,
            @"SELECT TABLE_NAME, COLUMN_NAME, IS_NULLABLE
              FROM information_schema.COLUMNS
              WHERE TABLE_SCHEMA = DATABASE()
              ORDER BY TABLE_NAME, ORDINAL_POSITION;",
            r =>
            {
                var t = r.GetString(0);
                if (!cols.TryGetValue(t, out var list))
                {
                    cols[t] = list = new();
                }

                list.Add(new ColumnSnapshot(r.GetString(1), r.GetString(2) == "YES"));
            }, cancellationToken);

        var pks = new Dictionary<string, List<string>>();
        await Query(connection,
            @"SELECT TABLE_NAME, COLUMN_NAME
              FROM information_schema.KEY_COLUMN_USAGE
              WHERE TABLE_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'PRIMARY'
              ORDER BY TABLE_NAME, ORDINAL_POSITION;",
            r => { var t = r.GetString(0); GetOrAdd(pks, t).Add(r.GetString(1)); }, cancellationToken);

        var fks = new Dictionary<string, List<ForeignKeySnapshot>>();
        await Query(connection,
            @"SELECT TABLE_NAME, COLUMN_NAME, REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME
              FROM information_schema.KEY_COLUMN_USAGE
              WHERE TABLE_SCHEMA = DATABASE() AND REFERENCED_TABLE_NAME IS NOT NULL;",
            r =>
            {
                var t = r.GetString(0);
                GetOrAdd(fks, t)
                    .Add(new ForeignKeySnapshot(new[] { r.GetString(1) }, r.GetString(2), new[] { r.GetString(3) }));
            }, cancellationToken);

        // Group STATISTICS rows into one index per (TABLE_NAME, INDEX_NAME), columns ordered by SEQ_IN_INDEX.
        var idxCols = new Dictionary<(string Table, string Index), List<(int Seq, string Col)>>();
        await Query(connection,
            @"SELECT TABLE_NAME, INDEX_NAME, COLUMN_NAME, SEQ_IN_INDEX
              FROM information_schema.STATISTICS
              WHERE TABLE_SCHEMA = DATABASE();",
            r =>
            {
                var key = (r.GetString(0), r.GetString(1));
                if (!idxCols.TryGetValue(key, out var list))
                {
                    idxCols[key] = list = new();
                }

                list.Add((Convert.ToInt32(r.GetValue(3), CultureInfo.CurrentCulture), r.GetString(2)));
            }, cancellationToken);

        var idx = new Dictionary<string, List<IndexSnapshot>>();
        foreach (var kv in idxCols)
        {
            var c = kv.Value.OrderBy(x => x.Seq).Select(x => x.Col).ToArray();
            var t = kv.Key.Table;
            GetOrAdd(idx, t).Add(new IndexSnapshot(c));
        }

        var tables = cols.Keys.Select(t => new TableSnapshot(
            t, cols[t],
            pks.TryGetValue(t, out var p) ? p : new List<string>(),
            fks.TryGetValue(t, out var f) ? f : new List<ForeignKeySnapshot>(),
            idx.TryGetValue(t, out var ii) ? ii : new List<IndexSnapshot>())).ToList();

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
