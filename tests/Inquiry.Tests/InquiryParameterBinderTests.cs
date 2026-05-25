using Inquiry.Parameters;
using System.Data;
using System.Data.Common;

namespace Inquiry.Tests;

public sealed class InquiryParameterBinderTests
{
    [Theory]
    [InlineData("Name", "@Name")]
    [InlineData("@Name", "@Name")]
    [InlineData(":Name", ":Name")]
    [InlineData("$Name", "$Name")]
    [InlineData("?", "?")]
    public void BindNormalizesParameterNamePrefix(string input, string expected)
    {
        using var command = new FakeDbCommand();

        InquiryParameterBinder.Bind(command, new[] { new InquiryParameter(input, 1) });

        Assert.Equal(expected, command.Parameters[0].ParameterName);
    }

    [Fact]
    public void BindSubstitutesDbNullForNullValue()
    {
        using var command = new FakeDbCommand();

        InquiryParameterBinder.Bind(command, new[] { new InquiryParameter("Name", null) });

        Assert.Equal(DBNull.Value, command.Parameters[0].Value);
    }

    [Fact]
    public void BindPropagatesAllOptionalProperties()
    {
        using var command = new FakeDbCommand();
        var parameter = new InquiryParameter(
            "Amount",
            123.45m,
            DbType.Decimal,
            ParameterDirection.Input,
            size: 10,
            precision: 18,
            scale: 4);

        InquiryParameterBinder.Bind(command, new[] { parameter });

        var bound = (FakeDbParameter)command.Parameters[0]!;
        Assert.Equal(DbType.Decimal, bound.DbType);
        Assert.Equal(ParameterDirection.Input, bound.Direction);
        Assert.Equal(10, bound.Size);
        Assert.Equal((byte)18, bound.Precision);
        Assert.Equal((byte)4, bound.Scale);
    }

    [Fact]
    public void BindAppendsMultipleParametersInOrder()
    {
        using var command = new FakeDbCommand();

        InquiryParameterBinder.Bind(command, new[]
        {
            new InquiryParameter("First", 1),
            new InquiryParameter("Second", 2),
            new InquiryParameter("Third", 3),
        });

        Assert.Equal(3, command.Parameters.Count);
        Assert.Equal("@First", command.Parameters[0].ParameterName);
        Assert.Equal("@Second", command.Parameters[1].ParameterName);
        Assert.Equal("@Third", command.Parameters[2].ParameterName);
    }

    [Fact]
    public void BindLeavesOptionalsUntouchedWhenNull()
    {
        // When InquiryParameter optionals are null, the binder should never assign them on the
        // underlying DbParameter — letting provider defaults stand.
        using var command = new FakeDbCommand();

        InquiryParameterBinder.Bind(command, new[] { new InquiryParameter("Name", "alpha") });

        var bound = (FakeDbParameter)command.Parameters[0]!;
        Assert.False(bound.DbTypeAssigned);
        Assert.False(bound.DirectionAssigned);
        Assert.False(bound.SizeAssigned);
        Assert.False(bound.PrecisionAssigned);
        Assert.False(bound.ScaleAssigned);
    }

    // --- Minimal in-memory ADO.NET fake — records every property assignment so tests can
    // distinguish "left default" from "explicitly set to default". This avoids depending on
    // provider quirks (SqliteParameter clamps Precision/Scale to zero, for example).
    private sealed class FakeDbCommand : DbCommand
    {
        private readonly FakeParameterCollection _parameters = new();

        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction? DbTransaction { get; set; }

        public new FakeParameterCollection Parameters => _parameters;
        public override void Cancel() { }
        public override int ExecuteNonQuery() => 0;
        public override object? ExecuteScalar() => null;
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new FakeDbParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            => throw new NotSupportedException();
    }

    private sealed class FakeParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _items = new();
        public override int Count => _items.Count;
        public override object SyncRoot { get; } = new();
        public override int Add(object value) { _items.Add((DbParameter)value); return _items.Count - 1; }
        public override void AddRange(Array values) { foreach (var v in values) Add(v!); }
        public override void Clear() => _items.Clear();
        public override bool Contains(object value) => _items.Contains((DbParameter)value);
        public override bool Contains(string value) => _items.Any(p => p.ParameterName == value);
        public override void CopyTo(Array array, int index) => ((Array)_items.ToArray()).CopyTo(array, index);
        public override System.Collections.IEnumerator GetEnumerator() => _items.GetEnumerator();
        public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);
        public override int IndexOf(string parameterName) => _items.FindIndex(p => p.ParameterName == parameterName);
        public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
        public override void Remove(object value) => _items.Remove((DbParameter)value);
        public override void RemoveAt(int index) => _items.RemoveAt(index);
        public override void RemoveAt(string parameterName) => _items.RemoveAll(p => p.ParameterName == parameterName);
        protected override DbParameter GetParameter(int index) => _items[index];
        protected override DbParameter GetParameter(string parameterName)
            => _items.First(p => p.ParameterName == parameterName);
        protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value)
            => _items[IndexOf(parameterName)] = value;
        public new DbParameter this[int index] => _items[index];
    }

    private sealed class FakeDbParameter : DbParameter
    {
        private DbType _dbType;
        private ParameterDirection _direction = ParameterDirection.Input;
        private int _size;
        private byte _precision;
        private byte _scale;

        public bool DbTypeAssigned { get; private set; }
        public bool DirectionAssigned { get; private set; }
        public bool SizeAssigned { get; private set; }
        public bool PrecisionAssigned { get; private set; }
        public bool ScaleAssigned { get; private set; }

        public override DbType DbType { get => _dbType; set { _dbType = value; DbTypeAssigned = true; } }
        public override ParameterDirection Direction { get => _direction; set { _direction = value; DirectionAssigned = true; } }
        public override bool IsNullable { get; set; }
        public override string ParameterName { get; set; } = string.Empty;
        public override int Size { get => _size; set { _size = value; SizeAssigned = true; } }
        public override string SourceColumn { get; set; } = string.Empty;
        public override bool SourceColumnNullMapping { get; set; }
        public override object? Value { get; set; }
        public override byte Precision { get => _precision; set { _precision = value; PrecisionAssigned = true; } }
        public override byte Scale { get => _scale; set { _scale = value; ScaleAssigned = true; } }
        public override void ResetDbType() { _dbType = default; DbTypeAssigned = false; }
    }
}
