using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.IntegrationTesting;

namespace Inquiry.Sqlite.Tests.Fixtures;

public sealed class SqliteSchemaIntrospector : ISchemaIntrospector
{
    public async Task<SchemaSnapshot> ReadAsync(DbConnection connection, CancellationToken ct = default)
    {
        var tableNames = new List<string>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) tableNames.Add(r.GetString(0));
        }

        var tables = new List<TableSnapshot>();
        foreach (var table in tableNames)
        {
            var columns = new List<ColumnSnapshot>();
            var pk = new List<(int Seq, string Col)>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    var name = r.GetString(1);
                    var notNull = r.GetInt32(3) == 1;
                    var pkOrd = r.GetInt32(5);
                    // SQLite reports INTEGER PRIMARY KEY AUTOINCREMENT (rowid alias) columns with
                    // notnull=0, but a primary-key column can never be null — treat PK members as NOT NULL.
                    columns.Add(new ColumnSnapshot(name, !notNull && pkOrd == 0));
                    if (pkOrd > 0) pk.Add((pkOrd, name));
                }
            }
            pk.Sort((a, b) => a.Seq.CompareTo(b.Seq));

            var fks = new List<ForeignKeySnapshot>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA foreign_key_list(\"{table}\");";
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    fks.Add(new ForeignKeySnapshot(new[] { r.GetString(3) }, r.GetString(2), new[] { r.GetString(4) }));
            }

            var indexes = new List<IndexSnapshot>();
            var indexNames = new List<string>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA index_list(\"{table}\");";
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct)) indexNames.Add(r.GetString(1));
            }
            foreach (var ix in indexNames)
            {
                var cols = new List<string>();
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = $"PRAGMA index_info(\"{ix}\");";
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct)) cols.Add(r.GetString(2));
                if (cols.Count > 0) indexes.Add(new IndexSnapshot(cols));
            }

            tables.Add(new TableSnapshot(table, columns, pk.ConvertAll(x => x.Col), fks, indexes));
        }

        return new SchemaSnapshot(tables);
    }
}
