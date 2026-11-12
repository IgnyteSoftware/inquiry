using System.Collections;
using System.Data.Common;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Inquiry.Benchmarks.SqlServer.LinqToDb;
using Inquiry.Northwind.Models;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider.SqlServer;
using Microsoft.Data.SqlClient;

namespace Inquiry.Benchmarks.SqlServer;

/// <summary>
/// Bulk copy of <see cref="Rows"/> shippers on SQL Server through <c>SqlBulkCopy</c>: handwritten ADO.NET
/// (baseline), linq2db <c>BulkCopyAsync</c> and the generated <c>[InquiryBulkInsert]</c> method. Each
/// iteration deletes the shippers first, so table growth does not skew the timings. Requires Docker.
/// </summary>
/// <remarks>
/// The baseline feeds the rows through a data reader and maps the columns by name, as the Inquiry
/// provider does, so both legs do the same <c>SqlBulkCopy</c> work. Neither leg turns on
/// <c>EnableStreaming</c>. Dapper and EF Core have no bulk-copy API, and RepoDB has it in a
/// package that this project does not reference, so they have no legs. The container is shared by the
/// whole process, and this class deletes the shippers that other classes read. Under
/// <c>--inProcess</c> run it with its own filter.
/// </remarks>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class BulkCopyBenchmarks
{
    private SqlServerBenchmarkDatabase _db = null!;
    private DataOptions _linqToDbOptions = null!;
    private Shipper[] _rows = null!;
    private L2Shipper[] _linqToDbRows = null!;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "BDN1302", Justification = "The single value keeps the parameter in the benchmark result names, so results stay comparable across runs.")]
    [Params(5000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = SqlServerBenchmarkDatabase.CreateAsync(seedRows: 1).GetAwaiter().GetResult();
        _linqToDbOptions = new DataOptions().UseSqlServer(_db.ConnectionString, SqlServerVersion.v2022, SqlServerProvider.MicrosoftDataSqlClient);

        _rows = new Shipper[Rows];
        _linqToDbRows = new L2Shipper[Rows];
        for (var i = 0; i < Rows; i++)
        {
            _rows[i] = new Shipper { CompanyName = "Bulk Shipper " + i, Phone = "(503) 555-" + (1000 + i % 9000) };
            _linqToDbRows[i] = new L2Shipper { CompanyName = _rows[i].CompanyName, Phone = _rows[i].Phone };
        }

        VerifyLegsAgreeAsync().GetAwaiter().GetResult();
    }

    [IterationSetup]
    public void IterationSetup()
        => _db.Inquiry.ExecuteAsync($"DELETE FROM [Shippers]").GetAwaiter().GetResult();

    [GlobalCleanup]
    public void GlobalCleanup()
        => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    // A leg that copies nothing would otherwise only show up as a suspiciously fast result.
    private async Task VerifyLegsAgreeAsync()
    {
        foreach (var leg in new Func<Task<long>>[] { BulkCopy_AdoNet, BulkCopy_LinqToDb, BulkCopy_Inquiry })
        {
            IterationSetup();
            var reported = await leg();
            var stored = await _db.Inquiry.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM [Shippers]");
            if (reported != Rows || stored != Rows)
            {
                throw new InvalidOperationException($"{leg.Method.Name} reported {reported} rows and the table holds {stored}. Expected {Rows}.");
            }
        }
    }

    [BenchmarkCategory("BulkCopy"), Benchmark(Baseline = true)]
    public async Task<long> BulkCopy_AdoNet()
    {
        await using var connection = new SqlConnection(_db.ConnectionString);
        await connection.OpenAsync();
        using var bulk = new SqlBulkCopy(connection) { DestinationTableName = "[Shippers]" };
        bulk.ColumnMappings.Add(0, "CompanyName");
        bulk.ColumnMappings.Add(1, "Phone");
        using var reader = new ShipperReader(_rows);
        await bulk.WriteToServerAsync(reader);
        return reader.RowsRead;
    }

    [BenchmarkCategory("BulkCopy"), Benchmark]
    public async Task<long> BulkCopy_LinqToDb()
    {
        await using var dc = new DataConnection(_linqToDbOptions);
        var result = await dc.BulkCopyAsync(new BulkCopyOptions { BulkCopyType = BulkCopyType.ProviderSpecific }, _linqToDbRows);
        return result.RowsCopied;
    }

    [BenchmarkCategory("BulkCopy"), Benchmark]
    public Task<long> BulkCopy_Inquiry()
        => _db.Shippers.BulkInsertAsync(_rows);

    /// <summary>Forward-only reader over the two copied columns. Bulk copy reads nothing else.</summary>
    private sealed class ShipperReader(Shipper[] rows) : DbDataReader
    {
        private int _index = -1;

        public int RowsRead => Math.Min(_index, rows.Length);

        public override int FieldCount => 2;
        public override bool HasRows => rows.Length > 0;
        public override bool IsClosed => false;
        public override int Depth => 0;
        public override int RecordsAffected => -1;
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));

        public override bool Read() => ++_index < rows.Length;
        public override bool NextResult() => false;
        public override object GetValue(int ordinal) => (ordinal == 0 ? rows[_index].CompanyName : rows[_index].Phone) ?? (object)DBNull.Value;
        public override bool IsDBNull(int ordinal) => ordinal == 1 && rows[_index].Phone is null;
        public override string GetString(int ordinal) => (string)GetValue(ordinal);
        public override string GetName(int ordinal) => ordinal == 0 ? "CompanyName" : "Phone";
        public override int GetOrdinal(string name) => name == "CompanyName" ? 0 : 1;
        public override Type GetFieldType(int ordinal) => typeof(string);
        public override string GetDataTypeName(int ordinal) => "nvarchar";

        public override int GetValues(object[] values) => throw new NotSupportedException();
        public override IEnumerator GetEnumerator() => throw new NotSupportedException();
        public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public override byte GetByte(int ordinal) => throw new NotSupportedException();
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override char GetChar(int ordinal) => throw new NotSupportedException();
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();
        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
        public override double GetDouble(int ordinal) => throw new NotSupportedException();
        public override float GetFloat(int ordinal) => throw new NotSupportedException();
        public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public override short GetInt16(int ordinal) => throw new NotSupportedException();
        public override int GetInt32(int ordinal) => throw new NotSupportedException();
        public override long GetInt64(int ordinal) => throw new NotSupportedException();
    }
}
