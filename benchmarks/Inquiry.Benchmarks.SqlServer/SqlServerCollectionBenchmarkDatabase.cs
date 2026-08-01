using System.Collections;
using System.Data;
using System.Data.Common;
using Inquiry.Benchmarks.Contracts.Fixtures;
using Inquiry.Northwind;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Inquiry.Benchmarks.SqlServer;

public sealed class SqlServerCollectionBenchmarkDatabase : IAsyncDisposable
{
    private static readonly string[] InsertOrder =
    [
        "Categories", "Customers", "CustomerDemographics", "Region", "Shippers", "Suppliers",
        "Employees", "Territories", "Products", "Orders", "CustomerCustomerDemo",
        "EmployeeTerritories", "Order Details",
    ];

    private readonly MsSqlContainer _container;

    private SqlServerCollectionBenchmarkDatabase(MsSqlContainer container, string connectionString, string tvpTypeName)
    {
        _container = container;
        ConnectionString = connectionString;
        TvpTypeName = tvpTypeName;
    }

    public string ConnectionString { get; }
    public string TvpTypeName { get; }

    public static async Task<SqlServerCollectionBenchmarkDatabase> CreateAsync(bool applyProviderArtifacts = true)
    {
        var image = DatabaseImageCatalog.GetRequired("sqlserver");
        var container = new MsSqlBuilder(image.Reference).Build();
        await container.StartAsync().ConfigureAwait(false);
        try
        {
            var database = "InquiryCollection_" + Guid.NewGuid().ToString("N");
            var admin = new SqlConnectionStringBuilder(container.GetConnectionString());
            await using (var connection = new SqlConnection(admin.ConnectionString))
            {
                await connection.OpenAsync().ConfigureAwait(false);
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE [{database}]";
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            admin.InitialCatalog = database;
            var connectionString = admin.ConnectionString;
            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync().ConfigureAwait(false);
                await ExecuteAsync(connection, NorthwindSchema.SqlServerDdl).ConfigureAwait(false);
                if (applyProviderArtifacts)
                    await ExecuteAsync(connection, global::Inquiry.Generated.InquiryGeneratedSchema.ProviderArtifactsDdl).ConfigureAwait(false);
            }

            var tvpTypeName = await ResolveTvpTypeNameAsync(connectionString).ConfigureAwait(false);
            await SeedStandardFixtureAsync(connectionString).ConfigureAwait(false);
            await ValidateFixtureAsync(connectionString).ConfigureAwait(false);
            return new SqlServerCollectionBenchmarkDatabase(container, connectionString, tvpTypeName);
        }
        catch
        {
            await container.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task SeedStandardFixtureAsync(string connectionString)
    {
        var manifest = NorthwindFixtureCatalog.For(FixtureTier.Standard);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        foreach (var tableName in InsertOrder)
        {
            var schema = NorthwindFixtureCatalog.Schema.Tables.Single(table => table.Name == tableName);
            using var checksum = new FixtureChecksumAccumulator();
            await using var rows = new SeedRowReader(
                NorthwindFixtureGenerator.Generate(tableName, FixtureTier.Standard, manifest.Seed), schema, checksum);
            var options = SqlBulkCopyOptions.TableLock;
            if (schema.Columns.Any(static column => column.IsGenerated)) options |= SqlBulkCopyOptions.KeepIdentity;
            using var bulk = new SqlBulkCopy(connection, options, null)
            {
                DestinationTableName = $"[{tableName}]",
                BatchSize = 1_000,
                BulkCopyTimeout = 0,
                EnableStreaming = true,
            };
            foreach (var column in schema.Columns)
                bulk.ColumnMappings.Add(column.Name, column.Name);
            await bulk.WriteToServerAsync(rows).ConfigureAwait(false);
            var actualChecksum = checksum.GetHashAndReset();
            if (!StringComparer.Ordinal.Equals(actualChecksum, manifest.TableChecksums[tableName]))
                throw new InvalidOperationException($"Generated checksum drifted while streaming table '{tableName}'.");
            if (schema.Columns.Any(static column => column.IsGenerated))
                await ExecuteAsync(connection,
                    $"DBCC CHECKIDENT ('[{tableName}]', RESEED, {manifest.RowCounts[tableName]}) WITH NO_INFOMSGS;")
                    .ConfigureAwait(false);
        }

        foreach (var table in NorthwindFixtureCatalog.Schema.Tables)
            await ExecuteAsync(connection, $"UPDATE STATISTICS [{table.Name}] WITH FULLSCAN;").ConfigureAwait(false);
    }

    private static async Task ValidateFixtureAsync(string connectionString)
    {
        var manifest = NorthwindFixtureCatalog.For(FixtureTier.Standard);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        foreach (var table in NorthwindFixtureCatalog.Schema.Tables)
        {
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 0;
            command.CommandText = $"SELECT {string.Join(", ", table.Columns.Select(static column => $"[{column.Name}]"))} " +
                                  $"FROM [{table.Name}] ORDER BY {FixtureOrderBy(table.Name)};";
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess)
                .ConfigureAwait(false);
            using var checksum = new FixtureChecksumAccumulator();
            using var expectedRows = NorthwindFixtureGenerator.Generate(table.Name, FixtureTier.Standard, manifest.Seed).GetEnumerator();
            var ordinal = 0;
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                ordinal++;
                if (!expectedRows.MoveNext())
                    throw new InvalidOperationException($"Persisted fixture has unexpected rows for '{table.Name}'.");
                var expectedRow = expectedRows.Current;
                var values = new SortedDictionary<string, object?>(StringComparer.Ordinal);
                for (var columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
                {
                    var columnName = table.Columns[columnIndex].Name;
                    var value = reader.IsDBNull(columnIndex) ? null : reader.GetValue(columnIndex);
                    if (value is DateTime dateTime) value = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
                    if (value is decimal decimalValue && expectedRow.Values[columnName] is decimal expectedDecimal)
                    {
                        if (decimalValue != expectedDecimal)
                            throw new InvalidOperationException($"Persisted fixture decimal drift for '{table.Name}.{columnName}' at ordinal {ordinal}.");
                        value = expectedDecimal;
                    }
                    values.Add(columnName, value);
                }
                var actualRow = new SeedRow(table.Name, ordinal, values);
                if (!actualRow.Equals(expectedRow))
                    throw new InvalidOperationException($"Persisted fixture row drift for '{table.Name}' at ordinal {ordinal}.");
                checksum.Append(actualRow);
            }
            if (ordinal != manifest.RowCounts[table.Name])
                throw new InvalidOperationException($"Fixture row-count drift for '{table.Name}': expected {manifest.RowCounts[table.Name]}, found {ordinal}.");
            if (!StringComparer.Ordinal.Equals(checksum.GetHashAndReset(), manifest.TableChecksums[table.Name]))
                throw new InvalidOperationException($"Persisted fixture checksum drift for '{table.Name}'.");
        }
    }

    private static string FixtureOrderBy(string tableName) => tableName switch
    {
        "Categories" => "[CategoryID]",
        "Customers" => "[CustomerID]",
        "CustomerCustomerDemo" => "[CustomerID], [CustomerTypeID]",
        "CustomerDemographics" => "[CustomerTypeID]",
        "Employees" => "[EmployeeID]",
        "EmployeeTerritories" => "[TerritoryID], [EmployeeID]",
        "Orders" => "[OrderID]",
        "Order Details" => $"[OrderID], CASE [ProductID] " + string.Join(" ", Enumerable.Range(0, 5).Select(slot =>
            $"WHEN (([OrderID] * 17 + {slot + NorthwindFixtureCatalog.Seed % 31}) % {NorthwindFixtureCatalog.For(FixtureTier.Standard).RowCounts["Products"]}) + 1 THEN {slot}")) + " END",
        "Products" => "[ProductID]",
        "Region" => "[RegionID]",
        "Shippers" => "[ShipperID]",
        "Suppliers" => "[SupplierID]",
        "Territories" => "[TerritoryID]",
        _ => throw new ArgumentOutOfRangeException(nameof(tableName), tableName, "Unknown fixture table."),
    };

    private static async Task<string> ResolveTvpTypeNameAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var type = connection.CreateCommand();
        type.CommandText = """
            SELECT QUOTENAME(SCHEMA_NAME(tt.schema_id)) + N'.' + QUOTENAME(tt.name)
            FROM sys.table_types AS tt
            JOIN sys.columns AS c ON c.object_id = tt.type_table_object_id
            JOIN sys.types AS st ON st.user_type_id = c.system_type_id
            GROUP BY tt.schema_id, tt.name
            HAVING COUNT(*) = 1
               AND MIN(c.name) = N'Value'
               AND MIN(st.name) = N'int'
               AND MIN(CONVERT(int, c.is_nullable)) = 0;
            """;
        var names = new List<string>();
        await using var reader = await type.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false)) names.Add(reader.GetString(0));
        if (names.Count != 1)
            throw new InvalidOperationException(
                "SQL Server collection benchmark requires exactly one generated single-column INT NOT NULL TVP artifact before collection; run the generated provider artifact DDL.");
        return names[0];
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return;
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 0;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    private sealed class SeedRowReader : DbDataReader
    {
        private readonly IEnumerator<SeedRow> _rows;
        private readonly FixtureTableSchema _schema;
        private readonly FixtureChecksumAccumulator _checksum;
        private SeedRow? _current;

        public SeedRowReader(IEnumerable<SeedRow> rows, FixtureTableSchema schema, FixtureChecksumAccumulator checksum)
        {
            _rows = rows.GetEnumerator();
            _schema = schema;
            _checksum = checksum;
        }

        public override int FieldCount => _schema.Columns.Count;
        public override bool HasRows => true;
        public override bool IsClosed => false;
        public override int RecordsAffected => -1;
        public override int Depth => 0;
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));
        public override string GetName(int ordinal) => _schema.Columns[ordinal].Name;
        public override int GetOrdinal(string name) => _schema.Columns.ToList().FindIndex(column => column.Name == name);
        public override Type GetFieldType(int ordinal) => _schema.Columns[ordinal].ClrType switch
        {
            "String" => typeof(string),
            "Int32" => typeof(int),
            "Int16" => typeof(short),
            "Decimal" => typeof(decimal),
            "Single" => typeof(float),
            "DateTime" => typeof(DateTime),
            "Boolean" => typeof(bool),
            var type => throw new InvalidOperationException($"Unknown fixture CLR type '{type}'."),
        };
        public override string GetDataTypeName(int ordinal) => _schema.Columns[ordinal].DatabaseType;
        public override object GetValue(int ordinal) => _current!.Values[GetName(ordinal)] ?? DBNull.Value;
        public override int GetValues(object[] values)
        {
            var count = Math.Min(values.Length, FieldCount);
            for (var index = 0; index < count; index++) values[index] = GetValue(index);
            return count;
        }
        public override bool IsDBNull(int ordinal) => GetValue(ordinal) is DBNull;
        public override bool Read()
        {
            if (!_rows.MoveNext()) return false;
            _current = _rows.Current;
            _checksum.Append(_current);
            return true;
        }
        public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Read());
        public override bool NextResult() => false;
        public override IEnumerator GetEnumerator() => throw new NotSupportedException();
        public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
        public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override char GetChar(int ordinal) => (char)GetValue(ordinal);
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
        public override short GetInt16(int ordinal) => (short)GetValue(ordinal);
        public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
        public override long GetInt64(int ordinal) => (long)GetValue(ordinal);
        public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
        public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
        public override string GetString(int ordinal) => (string)GetValue(ordinal);
        public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
        public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
        public override void Close() { }
    }
}
