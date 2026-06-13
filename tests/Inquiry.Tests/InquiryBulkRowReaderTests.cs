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
    }

    private static readonly InquiryBulkInsertDefinition<Row> Definition = new(
        schema: null,
        table: "Rows",
        columns: new[] { "Name", "Count" },
        getValue: static (row, i) => i switch
        {
            0 => row.Name,
            1 => (object?)row.Count ?? DBNull.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(i)),
        });

    [Fact]
    public void ReadsRowsForwardOnlyAndCountsThem()
    {
        var rows = new List<Row> { new() { Name = "a", Count = 1 }, new() { Name = "b", Count = null } };
        using var reader = new InquiryBulkRowReader<Row>(Definition, rows);

        Assert.Equal(2, reader.FieldCount);

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
    }
}
