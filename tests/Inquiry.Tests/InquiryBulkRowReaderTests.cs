using System;
using System.Collections.Generic;
using Inquiry.BulkCopy;
using Xunit;

namespace Inquiry.Tests;

/// <summary>
/// <see cref="InquiryBulkRowReader{TEntity}"/> — the DbDataReader adapter SqlBulkCopy/MySqlBulkCopy
/// consume: forward-only reads, ordinal values with DBNull semantics, name lookup, and the
/// rows-read tally that becomes the bulk insert's return value.
/// </summary>
public sealed class InquiryBulkRowReaderTests
{
    private sealed class Row
    {
        public string Name { get; init; } = string.Empty;
        public int? Count { get; init; }
        public byte[] Data { get; init; } = Array.Empty<byte>();
        public string Text { get; init; } = string.Empty;
    }

    private static readonly InquiryBulkInsertDefinition<Row> Definition = new(
        schema: null,
        table: "Rows",
        columns: new[] { "Name", "Count", "Data", "Text" },
        getValue: static (row, i) => i switch
        {
            0 => row.Name,
            1 => (object?)row.Count ?? DBNull.Value,
            2 => row.Data,
            3 => row.Text,
            _ => throw new ArgumentOutOfRangeException(nameof(i)),
        },
        columnTypes: new[] { System.Data.DbType.String, System.Data.DbType.Int32, System.Data.DbType.Binary, System.Data.DbType.String },
        fieldTypes: new[] { typeof(string), typeof(int), typeof(byte[]), typeof(string) },
        typedAccessors: new IInquiryBulkColumnAccessor<Row>[]
        {
            new InquiryBulkColumnAccessor<Row, string>(static row => row.Name),
            new InquiryBulkColumnAccessor<Row, int>(static row => row.Count!.Value, static row => !row.Count.HasValue),
            new InquiryBulkColumnAccessor<Row, byte[]>(static row => row.Data),
            new InquiryBulkColumnAccessor<Row, string>(static row => row.Text),
        });

    [Fact]
    public void ReadsRowsForwardOnlyAndCountsThem()
    {
        var rows = new List<Row> { new() { Name = "a", Count = 1 }, new() { Name = "b", Count = null } };
        using var reader = new InquiryBulkRowReader<Row>(Definition, rows);

        Assert.Equal(4, reader.FieldCount);

        Assert.True(reader.Read());
        Assert.Equal("a", reader.GetValue(0));
        Assert.Equal(1, reader.GetValue(1));
        Assert.False(reader.IsDBNull(1));

        Assert.True(reader.Read());
        Assert.Equal("b", reader.GetString(0));
        Assert.True(reader.IsDBNull(1));
        Assert.Same(DBNull.Value, reader.GetValue(1));

        Assert.False(reader.Read());
        Assert.Equal(2L, reader.RowsRead);
    }

    [Fact]
    public void TypedGettersUseGeneratedAccessors()
    {
        var id = Guid.NewGuid();
        var timestamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var definition = new InquiryBulkInsertDefinition<TypedRow>(
            null,
            "TypedRows",
            new[] { "Boolean", "Byte", "Char", "DateTime", "Decimal", "Double", "Single", "Guid", "Int16", "Int32", "Int64", "String" },
            static (row, ordinal) => ordinal switch
            {
                0 => row.Boolean,
                1 => row.Byte,
                2 => row.Char,
                3 => row.DateTime,
                4 => row.Decimal,
                5 => row.Double,
                6 => row.Single,
                7 => row.Guid,
                8 => row.Int16,
                9 => row.Int32,
                10 => row.Int64,
                11 => row.String,
                _ => throw new ArgumentOutOfRangeException(nameof(ordinal)),
            },
            null,
            new[] { typeof(bool), typeof(byte), typeof(char), typeof(DateTime), typeof(decimal), typeof(double), typeof(float), typeof(Guid), typeof(short), typeof(int), typeof(long), typeof(string) },
            new IInquiryBulkColumnAccessor<TypedRow>[]
            {
                new InquiryBulkColumnAccessor<TypedRow, bool>(static row => row.Boolean),
                new InquiryBulkColumnAccessor<TypedRow, byte>(static row => row.Byte),
                new InquiryBulkColumnAccessor<TypedRow, char>(static row => row.Char),
                new InquiryBulkColumnAccessor<TypedRow, DateTime>(static row => row.DateTime),
                new InquiryBulkColumnAccessor<TypedRow, decimal>(static row => row.Decimal),
                new InquiryBulkColumnAccessor<TypedRow, double>(static row => row.Double),
                new InquiryBulkColumnAccessor<TypedRow, float>(static row => row.Single),
                new InquiryBulkColumnAccessor<TypedRow, Guid>(static row => row.Guid),
                new InquiryBulkColumnAccessor<TypedRow, short>(static row => row.Int16),
                new InquiryBulkColumnAccessor<TypedRow, int>(static row => row.Int32),
                new InquiryBulkColumnAccessor<TypedRow, long>(static row => row.Int64),
                new InquiryBulkColumnAccessor<TypedRow, string>(static row => row.String),
            });
        using var reader = new InquiryBulkRowReader<TypedRow>(definition, new[]
        {
            new TypedRow(true, 2, 'c', timestamp, 4.5m, 6.5, 7.5f, id, 8, 9, 10, "value"),
        });

        Assert.True(reader.Read());
        Assert.True(reader.GetBoolean(0));
        Assert.Equal((byte)2, reader.GetByte(1));
        Assert.Equal('c', reader.GetChar(2));
        Assert.Equal(timestamp, reader.GetDateTime(3));
        Assert.Equal(4.5m, reader.GetDecimal(4));
        Assert.Equal(6.5, reader.GetDouble(5));
        Assert.Equal(7.5f, reader.GetFloat(6));
        Assert.Equal(id, reader.GetGuid(7));
        Assert.Equal((short)8, reader.GetInt16(8));
        Assert.Equal(9, reader.GetInt32(9));
        Assert.Equal(10L, reader.GetInt64(10));
        Assert.Equal("value", reader.GetString(11));
        Assert.Equal(9, reader.GetFieldValue<int>(9));
    }

    [Fact]
    public void TypedAccessorNullColumnsFollowDbDataReaderNullContract()
    {
        var definition = new InquiryBulkInsertDefinition<Row>(
            null,
            "T",
            new[] { "Name", "Count" },
            static (row, i) => i switch
            {
                0 => (object?)row.Name ?? DBNull.Value,
                1 => (object?)row.Count ?? DBNull.Value,
                _ => throw new ArgumentOutOfRangeException(nameof(i)),
            },
            null,
            new[] { typeof(string), typeof(int) },
            new IInquiryBulkColumnAccessor<Row>[]
            {
                new InquiryBulkColumnAccessor<Row, string>(static row => row.Name, static row => row.Name is null),
                new InquiryBulkColumnAccessor<Row, int>(static row => row.Count!.Value, static row => !row.Count.HasValue),
            });
        using var reader = new InquiryBulkRowReader<Row>(definition, new[]
        {
            new Row { Name = null!, Count = null },
            new Row { Name = "present", Count = 3 },
        });

        Assert.True(reader.Read());
        Assert.Same(DBNull.Value, reader.GetFieldValue<object>(0));
        Assert.Same(DBNull.Value, reader.GetFieldValue<object>(1));
        Assert.Throws<InvalidCastException>(() => reader.GetString(0));
        Assert.Throws<InvalidCastException>(() => reader.GetInt32(1));

        Assert.True(reader.Read());
        Assert.Equal("present", reader.GetString(0));
        Assert.Equal(3, reader.GetInt32(1));
    }

    [Fact]
    public void ResolvesColumnNamesCaseInsensitively()
    {
        using var reader = new InquiryBulkRowReader<Row>(Definition, Array.Empty<Row>());

        Assert.Equal("Name", reader.GetName(0));
        Assert.Equal(1, reader.GetOrdinal("count"));
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("Missing"));
    }

    [Fact]
    public void ValueAccessBeforeReadThrows()
    {
        using var reader = new InquiryBulkRowReader<Row>(Definition, new[] { new Row { Name = "x" } });
        Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));
    }

    [Fact]
    public void DefinitionValidatesArguments()
    {
        Assert.Throws<ArgumentException>(() => new InquiryBulkInsertDefinition<Row>(null, "T", Array.Empty<string>(), static (_, _) => DBNull.Value));
        Assert.Throws<ArgumentNullException>(() => new InquiryBulkInsertDefinition<Row>(null, "T", new[] { "C" }, null!));
        Assert.Throws<ArgumentException>(() => new InquiryBulkInsertDefinition<Row>(null, "T", new[] { "C" }, static (_, _) => 1, Array.Empty<System.Data.DbType>()));
        Assert.Throws<ArgumentException>(() => new InquiryBulkInsertDefinition<Row>(null, "T", new[] { "C" }, static (_, _) => 1, new[] { System.Data.DbType.Int32 }, Array.Empty<Type>()));
        Assert.Throws<ArgumentException>(() => new InquiryBulkInsertDefinition<Row>(
            null, "T", new[] { "C" }, static (_, _) => 1, null, null, new IInquiryBulkColumnAccessor<Row>[] { null! }));
    }

    [Fact]
    public void GeneratedFieldTypesAreStableBeforeDuringAndAfterRows()
    {
        using var reader = new InquiryBulkRowReader<Row>(Definition, new[]
        {
            new Row { Name = "first", Count = null },
            new Row { Name = "second", Count = 2 },
        });

        Assert.Equal(typeof(string), reader.GetFieldType(0));
        Assert.Equal(typeof(int), reader.GetFieldType(1));
        Assert.True(reader.Read());
        Assert.True(reader.IsDBNull(1));
        Assert.Equal(typeof(int), reader.GetFieldType(1));
        Assert.True(reader.Read());
        Assert.Equal(typeof(int), reader.GetFieldType(1));
        Assert.False(reader.Read());
        Assert.Equal(typeof(int), reader.GetFieldType(1));
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetFieldType(-1));
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetFieldType(reader.FieldCount));
    }

    [Fact]
    public void ManualDefinitionFallsBackToCurrentValueType()
    {
        var definition = new InquiryBulkInsertDefinition<Row>(null, "T", new[] { "Name" }, static (row, _) => row.Name);
        using var reader = new InquiryBulkRowReader<Row>(definition, new[] { new Row { Name = "value" } });

        Assert.Equal(typeof(object), reader.GetFieldType(0));
        Assert.True(reader.Read());
        Assert.Equal(typeof(string), reader.GetFieldType(0));
        Assert.False(reader.Read());
        Assert.Equal(typeof(object), reader.GetFieldType(0));
    }

    [Fact]
    public void GetBytesSupportsLengthProbesOffsetsPartialCopiesAndEmptyValues()
    {
        using var reader = new InquiryBulkRowReader<Row>(Definition, new[]
        {
            new Row { Data = new byte[] { 10, 20, 30, 40 } },
            new Row { Data = Array.Empty<byte>() },
        });
        Assert.True(reader.Read());

        Assert.Equal(4, reader.GetBytes(2, 0, null, 0, 0));
        Assert.Equal(4, reader.GetBytes(2, long.MaxValue, null, 0, 0));
        var destination = new byte[] { 1, 1, 1, 1, 1, 1 };
        Assert.Equal(2, reader.GetBytes(2, 1, destination, 2, 2));
        Assert.Equal(new byte[] { 1, 1, 20, 30, 1, 1 }, destination);
        Assert.Equal(1, reader.GetBytes(2, 3, destination, 0, 4));
        Assert.Equal(0, reader.GetBytes(2, 4, destination, 0, 4));
        Assert.Equal(0, reader.GetBytes(2, long.MaxValue, destination, 0, 4));
        var unchanged = (byte[])destination.Clone();
        Assert.Equal(0, reader.GetBytes(2, 0, destination, destination.Length, 0));
        Assert.Equal(unchanged, destination);

        Assert.True(reader.Read());
        Assert.Equal(0, reader.GetBytes(2, 0, null, 0, 0));
        Assert.Equal(0, reader.GetBytes(2, 0, destination, 0, destination.Length));
    }

    [Fact]
    public void GetCharsSupportsLengthProbesOffsetsPartialCopiesAndEmptyValues()
    {
        using var reader = new InquiryBulkRowReader<Row>(Definition, new[]
        {
            new Row { Text = "abcd" },
            new Row { Text = string.Empty },
        });
        Assert.True(reader.Read());

        Assert.Equal(4, reader.GetChars(3, 0, null, 0, 0));
        Assert.Equal(4, reader.GetChars(3, long.MaxValue, null, 0, 0));
        var destination = new[] { 'x', 'x', 'x', 'x', 'x', 'x' };
        Assert.Equal(2, reader.GetChars(3, 1, destination, 2, 2));
        Assert.Equal(new[] { 'x', 'x', 'b', 'c', 'x', 'x' }, destination);
        Assert.Equal(1, reader.GetChars(3, 3, destination, 0, 4));
        Assert.Equal(0, reader.GetChars(3, 4, destination, 0, 4));
        Assert.Equal(0, reader.GetChars(3, long.MaxValue, destination, 0, 4));
        var unchanged = (char[])destination.Clone();
        Assert.Equal(0, reader.GetChars(3, 0, destination, destination.Length, 0));
        Assert.Equal(unchanged, destination);

        Assert.True(reader.Read());
        Assert.Equal(0, reader.GetChars(3, 0, null, 0, 0));
        Assert.Equal(0, reader.GetChars(3, 0, destination, 0, destination.Length));
    }

    [Fact]
    public void StreamAccessRejectsInvalidOrdinalsTypesNullsAndRanges()
    {
        using var reader = new InquiryBulkRowReader<Row>(Definition, new[] { new Row { Count = null } });
        Assert.True(reader.Read());

        Assert.Throws<IndexOutOfRangeException>(() => reader.GetBytes(-1, 0, null, 0, 0));
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetChars(reader.FieldCount, 0, null, 0, 0));
        Assert.Throws<InvalidCastException>(() => reader.GetBytes(0, 0, null, 0, 0));
        Assert.Throws<InvalidCastException>(() => reader.GetChars(2, 0, null, 0, 0));
        Assert.Throws<InvalidCastException>(() => reader.GetBytes(1, 0, null, 0, 0));
        Assert.Throws<InvalidCastException>(() => reader.GetChars(1, 0, null, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetBytes(2, -1, null, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetBytes(2, 0, null, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetChars(3, 0, null, 0, -1));
        Assert.Throws<ArgumentException>(() => reader.GetBytes(2, 0, new byte[2], 1, 2));
        Assert.Throws<ArgumentException>(() => reader.GetChars(3, 0, new char[2], 3, 0));
    }

    [Fact]
    public void HasRowsUsesSingleLookaheadWithoutCountingUntilRead()
    {
        using var reader = new InquiryBulkRowReader<Row>(Definition, new[] { new Row { Name = "first" }, new Row { Name = "second" } });

        Assert.True(reader.HasRows);
        Assert.True(reader.HasRows);
        Assert.Equal(0, reader.RowsRead);
        Assert.True(reader.Read());
        Assert.Equal("first", reader.GetString(0));
        Assert.Equal(1, reader.RowsRead);
        Assert.True(reader.Read());
        Assert.Equal("second", reader.GetString(0));
        Assert.False(reader.Read());
        Assert.True(reader.HasRows);
        Assert.Equal(2, reader.RowsRead);
    }

    [Fact]
    public void EmptyReaderReportsNoRowsAndDisposalClosesReader()
    {
        var reader = new InquiryBulkRowReader<Row>(Definition, Array.Empty<Row>());
        Assert.False(reader.HasRows);
        Assert.Equal(0, reader.RowsRead);
        Assert.False(reader.Read());
        Assert.False(reader.IsClosed);

        reader.Dispose();

        Assert.True(reader.IsClosed);
        Assert.Throws<ObjectDisposedException>(() => reader.Read());
        Assert.Throws<ObjectDisposedException>(() => _ = reader.HasRows);
        Assert.Throws<ObjectDisposedException>(() => reader.GetValue(0));
        Assert.Throws<ObjectDisposedException>(() => reader.GetFieldType(0));
        Assert.Throws<ObjectDisposedException>(() => reader.GetBytes(2, 0, null, 0, 0));
        Assert.Throws<ObjectDisposedException>(() => reader.GetChars(3, 0, null, 0, 0));
        Assert.Throws<ObjectDisposedException>(() => reader.GetName(0));
        Assert.Throws<ObjectDisposedException>(() => reader.GetOrdinal("Name"));
        Assert.Throws<ObjectDisposedException>(() => reader.GetValues(Array.Empty<object>()));
        Assert.Throws<ObjectDisposedException>(() => reader.NextResult());
    }

    private sealed record TypedRow(
        bool Boolean,
        byte Byte,
        char Char,
        DateTime DateTime,
        decimal Decimal,
        double Double,
        float Single,
        Guid Guid,
        short Int16,
        int Int32,
        long Int64,
        string String);
}
