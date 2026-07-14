using Inquiry.Commands;
using Inquiry.SqlServer.Parameters;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Data.Common;

namespace Inquiry.SqlServer.Tests;

public sealed class InquiryTvpParameterTests
{
    private enum UnsignedState : uint { High = 3_000_000_000u, Max = uint.MaxValue }

    [Theory]
    [MemberData(nameof(UnsignedCases))]
    public void DirectUnsignedBinderUsesSignedArtifactMetadataAndRows(object values, SqlDbType expectedType, object[] expectedRows)
    {
        using var command = new SqlCommand();
        switch (values)
        {
            case sbyte[] typed: InquiryTvpParameter.Bind(command, "@v", typed, "[dbo].[Inquiry_Tvp_test]"); break;
            case ushort[] typed: InquiryTvpParameter.Bind(command, "@v", typed, "[dbo].[Inquiry_Tvp_test]"); break;
            case uint[] typed: InquiryTvpParameter.Bind(command, "@v", typed, "[dbo].[Inquiry_Tvp_test]"); break;
            case ulong[] typed: InquiryTvpParameter.Bind(command, "@v", typed, "[dbo].[Inquiry_Tvp_test]"); break;
            case UnsignedState[] typed: InquiryTvpParameter.Bind(command, "@v", typed, "[dbo].[Inquiry_Tvp_test]"); break;
        }

        var parameter = Assert.IsType<SqlParameter>(Assert.Single(command.Parameters.Cast<SqlParameter>()));
        var records = Assert.IsAssignableFrom<IEnumerable<Microsoft.Data.SqlClient.Server.SqlDataRecord>>(parameter.Value).ToArray();
        Assert.All(records, record => Assert.Equal(expectedType, record.GetSqlMetaData(0).SqlDbType));
        Assert.Equal(expectedRows, records.Select(record => record.GetValue(0)).ToArray());
    }

    public static IEnumerable<object[]> UnsignedCases()
    {
        yield return new object[] { new sbyte[] { -1 }, SqlDbType.TinyInt, new object[] { byte.MaxValue } };
        yield return new object[] { new ushort[] { ushort.MaxValue }, SqlDbType.SmallInt, new object[] { (short)-1 } };
        yield return new object[] { new uint[] { uint.MaxValue }, SqlDbType.Int, new object[] { -1 } };
        yield return new object[] { new ulong[] { ulong.MaxValue }, SqlDbType.BigInt, new object[] { -1L } };
        yield return new object[] { new[] { UnsignedState.High, UnsignedState.Max }, SqlDbType.Int, new object[] { unchecked((int)3_000_000_000u), -1 } };
    }

    [Fact]
    public void NullableUnsignedEmptyUsesNullValueAndAllNullRetainsDbNullRows()
    {
        using var empty = new SqlCommand();
        InquiryTvpParameter.Bind(empty, "@v", Array.Empty<uint?>(), "[dbo].[Inquiry_Tvp_test]");
        var emptyParameter = Assert.IsType<SqlParameter>(Assert.Single(empty.Parameters.Cast<SqlParameter>()));
        Assert.Equal(SqlDbType.Structured, emptyParameter.SqlDbType);
        Assert.Equal("[dbo].[Inquiry_Tvp_test]", emptyParameter.TypeName);
        Assert.Null(emptyParameter.Value);

        using var allNull = new SqlCommand();
        InquiryTvpParameter.Bind(allNull, "@v", new uint?[] { null, null }, "[dbo].[Inquiry_Tvp_test]");
        var allNullParameter = Assert.IsType<SqlParameter>(Assert.Single(allNull.Parameters.Cast<SqlParameter>()));
        Assert.Equal(SqlDbType.Structured, allNullParameter.SqlDbType);
        Assert.Equal("[dbo].[Inquiry_Tvp_test]", allNullParameter.TypeName);
        var records = Assert.IsAssignableFrom<IEnumerable<Microsoft.Data.SqlClient.Server.SqlDataRecord>>(allNullParameter.Value).ToArray();
        Assert.Equal(2, records.Length);
        Assert.All(records, record => Assert.True(record.IsDBNull(0)));
    }

    [Fact]
    public void NullableUnsignedAndEnumValuesPreserveFullBitRanges()
    {
        using var unsignedCommand = new SqlCommand();
        InquiryTvpParameter.Bind(unsignedCommand, "@v", new uint?[] { 0, 2_147_483_648u, uint.MaxValue, null }, "[dbo].[Inquiry_Tvp_test]");
        var unsignedRows = Assert.IsAssignableFrom<IEnumerable<Microsoft.Data.SqlClient.Server.SqlDataRecord>>(
            Assert.IsType<SqlParameter>(Assert.Single(unsignedCommand.Parameters.Cast<SqlParameter>())).Value).ToArray();
        Assert.Equal(new object[] { 0, int.MinValue, -1, DBNull.Value }, unsignedRows.Select(static row => row.GetValue(0)).ToArray());

        using var enumCommand = new SqlCommand();
        InquiryTvpParameter.Bind(enumCommand, "@v", new UnsignedState?[] { UnsignedState.High, UnsignedState.Max, null }, "[dbo].[Inquiry_Tvp_test]");
        var enumRows = Assert.IsAssignableFrom<IEnumerable<Microsoft.Data.SqlClient.Server.SqlDataRecord>>(
            Assert.IsType<SqlParameter>(Assert.Single(enumCommand.Parameters.Cast<SqlParameter>())).Value).ToArray();
        Assert.Equal(new object[] { unchecked((int)3_000_000_000u), -1, DBNull.Value }, enumRows.Select(static row => row.GetValue(0)).ToArray());
    }

    [Fact]
    public void UnsupportedBinderFailsWithoutOpeningConnection()
    {
        using var command = new SqlCommand();

        var exception = Assert.Throws<NotSupportedException>(() =>
            InquiryTvpParameter.BindUnsupported(command, "@values", new[] { DateOnly.MinValue }));

        Assert.Contains(typeof(DateOnly).FullName!, exception.Message);
        Assert.Null(command.Connection);
        Assert.Empty(command.Parameters.Cast<SqlParameter>());
    }

    [Fact]
    public void BindUsesExplicitQualifiedTypeWithoutConnectionIo()
    {
        using var command = new SqlCommand();

        InquiryTvpParameter.Bind(command, "@ids", new[] { 1, 2 }, "[tenant].[Inquiry_Tvp_test]");

        Assert.Null(command.Connection);
        var parameter = Assert.IsType<SqlParameter>(Assert.Single(command.Parameters.Cast<SqlParameter>()));
        Assert.Equal("@ids", parameter.ParameterName);
        Assert.Equal(SqlDbType.Structured, parameter.SqlDbType);
        Assert.Equal("[tenant].[Inquiry_Tvp_test]", parameter.TypeName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unqualified")]
    [InlineData("dbo.type.extra")]
    [InlineData("dbo.[type]")]
    [InlineData("[dbo].[type].")]
    [InlineData("[dbo].[type")]
    [InlineData("[].[]")]
    public void BindRejectsInvalidTypeName(string? typeName)
    {
        using var command = new SqlCommand();
        Assert.ThrowsAny<ArgumentException>(() =>
            InquiryTvpParameter.Bind(command, "@ids", Array.Empty<int>(), typeName!));
        Assert.Empty(command.Parameters.Cast<SqlParameter>());
    }

    [Theory]
    [InlineData("[schema with spaces].[Inquiry_Tvp_test]")]
    [InlineData("[9.leading].[Inquiry_Tvp_test]")]
    [InlineData("[schema.with.dot].[Inquiry_Tvp_test]")]
    [InlineData("[schema's].[Inquiry_Tvp_test]")]
    [InlineData("[schema]]name].[Inquiry_Tvp_test]")]
    public void BindAcceptsBracketEscapedGeneratedTypeNames(string typeName)
    {
        using var command = new SqlCommand();
        InquiryTvpParameter.Bind(command, "@ids", new[] { 1 }, typeName);
        Assert.Equal(typeName, Assert.IsType<SqlParameter>(Assert.Single(command.Parameters.Cast<SqlParameter>())).TypeName);
    }

    [Fact]
    public void BindRetainsNullAndEmptyCollectionSemantics()
    {
        using var nullCommand = new SqlCommand();
        InquiryTvpParameter.Bind<int>(nullCommand, "@ids", null, "[dbo].[Inquiry_Tvp_test]");
        Assert.Null(Assert.IsType<SqlParameter>(Assert.Single(nullCommand.Parameters.Cast<SqlParameter>())).Value);

        using var emptyCommand = new SqlCommand();
        InquiryTvpParameter.Bind(emptyCommand, "@ids", Array.Empty<int>(), "[dbo].[Inquiry_Tvp_test]");
        Assert.Null(Assert.IsType<SqlParameter>(Assert.Single(emptyCommand.Parameters.Cast<SqlParameter>())).Value);
    }

    [Fact]
    public void ExactBinderPeeksOnceThenStreamsOnePassWithoutReplay()
    {
        using var command = new SqlCommand();
        var source = new ProbeEnumerable<int>(new[] { 10, 20, 30 });
        var descriptor = InquiryTvpDescriptor.Get("int", 0, 10, 0, false);

        InquiryTvpParameter.Bind(command, "@ids", source, "[dbo].[Inquiry_Tvp_test]", descriptor);

        Assert.Equal(1, source.GetEnumeratorCount);
        Assert.Equal(1, source.MoveNextCount);
        Assert.Equal(0, source.CurrentCount);
        var parameter = Assert.IsType<SqlParameter>(Assert.Single(command.Parameters.Cast<SqlParameter>()));
        var records = Assert.IsAssignableFrom<IEnumerable<Microsoft.Data.SqlClient.Server.SqlDataRecord>>(parameter.Value);
        Assert.Equal(new[] { 10, 20, 30 }, records.Select(record => record.GetInt32(0)).ToArray());
        Assert.Equal(1, source.GetEnumeratorCount);
        Assert.Equal(4, source.MoveNextCount);
        Assert.Equal(3, source.CurrentCount);
        Assert.Equal(1, source.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => records.ToArray());
    }

    [Fact]
    public void NullableDescriptorRetainsNullRowsAndNonNullableDescriptorReportsIndex()
    {
        using var nullableCommand = new SqlCommand();
        InquiryTvpParameter.Bind(nullableCommand, "@ids", new int?[] { 1, null, 3 }, "[dbo].[Inquiry_Tvp_test]",
            InquiryTvpDescriptor.Get("int", 0, 10, 0, true));
        var nullableRows = Assert.IsAssignableFrom<IEnumerable<Microsoft.Data.SqlClient.Server.SqlDataRecord>>(
            Assert.IsType<SqlParameter>(Assert.Single(nullableCommand.Parameters.Cast<SqlParameter>())).Value).ToArray();
        Assert.Equal(3, nullableRows.Length);
        Assert.Equal(1, nullableRows[0].GetInt32(0));
        Assert.True(nullableRows[1].IsDBNull(0));
        Assert.Equal(3, nullableRows[2].GetInt32(0));

        using var nonNullableCommand = new SqlCommand();
        InquiryTvpParameter.Bind(nonNullableCommand, "@ids", new int?[] { 1, null }, "[dbo].[Inquiry_Tvp_test]",
            InquiryTvpDescriptor.Get("int", 0, 10, 0, false));
        var nonNullableRows = Assert.IsAssignableFrom<IEnumerable<Microsoft.Data.SqlClient.Server.SqlDataRecord>>(
            Assert.IsType<SqlParameter>(Assert.Single(nonNullableCommand.Parameters.Cast<SqlParameter>())).Value);
        var exception = Assert.Throws<InvalidOperationException>(() => nonNullableRows.ToArray());
        Assert.Contains("index 1", exception.Message);
    }

    [Fact]
    public void CommandResourceDisposalReleasesAbandonedSourceExactlyOnce()
    {
        using var command = new SqlCommand();
        var source = new ProbeEnumerable<int>(new[] { 1, 2 });
        InquiryTvpParameter.Bind(command, "@ids", source, "[dbo].[Inquiry_Tvp_test]",
            InquiryTvpDescriptor.Get("int", 0, 10, 0, false));

        InquiryCommandResources.Dispose(command);
        InquiryCommandResources.Dispose(command);

        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(1, source.MoveNextCount);
    }

    [Fact]
    public void BindTimeAndStreamingFailuresDisposeWithoutReplay()
    {
        using var firstCommand = new SqlCommand();
        var first = new ProbeEnumerable<int>(new[] { 1 }, failMoveNextAt: 1);
        Assert.Throws<ProbeException>(() => InquiryTvpParameter.Bind(firstCommand, "@ids", first, "[dbo].[Inquiry_Tvp_test]",
            InquiryTvpDescriptor.Get("int", 0, 10, 0, false)));
        Assert.Equal(1, first.DisposeCount);
        Assert.Empty(firstCommand.Parameters.Cast<SqlParameter>());

        using var laterCommand = new SqlCommand();
        var later = new ProbeEnumerable<int>(new[] { 1, 2 }, failMoveNextAt: 2);
        InquiryTvpParameter.Bind(laterCommand, "@ids", later, "[dbo].[Inquiry_Tvp_test]",
            InquiryTvpDescriptor.Get("int", 0, 10, 0, false));
        var rows = Assert.IsAssignableFrom<IEnumerable<Microsoft.Data.SqlClient.Server.SqlDataRecord>>(
            Assert.IsType<SqlParameter>(Assert.Single(laterCommand.Parameters.Cast<SqlParameter>())).Value);
        Assert.Throws<ProbeException>(() => rows.ToArray());
        Assert.Equal(1, later.DisposeCount);
        Assert.Equal(2, later.MoveNextCount);
    }

    [Fact]
    public void FirstMoveNextAndDisposeFailuresAreAggregatedPrimaryFirst()
    {
        using var command = new SqlCommand();
        var source = new ProbeEnumerable<int>(new[] { 1 }, failMoveNextAt: 1, failDispose: true);

        var exception = Assert.Throws<AggregateException>(() => InquiryTvpParameter.Bind(
            command, "@ids", source, "[dbo].[Inquiry_Tvp_test]",
            InquiryTvpDescriptor.Get("int", 0, 10, 0, false)));

        Assert.Collection(exception.InnerExceptions,
            static error => Assert.IsType<ProbeException>(error),
            static error => Assert.IsType<ProbeException>(error));
        Assert.Equal(1, source.DisposeCount);
        Assert.Empty(command.Parameters.Cast<SqlParameter>());
    }

    [Fact]
    public void WriterAndDisposeFailuresAreAggregatedPrimaryFirst()
    {
        using var command = new SqlCommand();
        var source = new ProbeEnumerable<int?>(new int?[] { null }, failDispose: true);
        InquiryTvpParameter.Bind(command, "@ids", source, "[dbo].[Inquiry_Tvp_test]",
            InquiryTvpDescriptor.Get("int", 0, 10, 0, false));
        var rows = Assert.IsAssignableFrom<IEnumerable<Microsoft.Data.SqlClient.Server.SqlDataRecord>>(
            Assert.IsType<SqlParameter>(Assert.Single(command.Parameters.Cast<SqlParameter>())).Value);

        var exception = Assert.Throws<AggregateException>(() => rows.ToArray());

        Assert.Collection(exception.InnerExceptions,
            static error => Assert.IsType<InvalidOperationException>(error),
            static error => Assert.IsType<ProbeException>(error));
        Assert.Equal(1, source.DisposeCount);
    }

    [Fact]
    public void CurrentAndDisposeFailuresAreAggregatedPrimaryFirst()
    {
        using var command = new SqlCommand();
        var source = new ProbeEnumerable<int>(new[] { 1 }, failCurrentAt: 1, failDispose: true);
        InquiryTvpParameter.Bind(command, "@ids", source, "[dbo].[Inquiry_Tvp_test]",
            InquiryTvpDescriptor.Get("int", 0, 10, 0, false));
        var rows = Assert.IsAssignableFrom<IEnumerable<Microsoft.Data.SqlClient.Server.SqlDataRecord>>(
            Assert.IsType<SqlParameter>(Assert.Single(command.Parameters.Cast<SqlParameter>())).Value);

        var exception = Assert.Throws<AggregateException>(() => rows.ToArray());

        Assert.Collection(exception.InnerExceptions,
            static error => Assert.IsType<ProbeException>(error),
            static error => Assert.IsType<ProbeException>(error));
        Assert.Equal(1, source.DisposeCount);
    }

    [Fact]
    public void ParameterAddAndDisposeFailuresAreAggregatedPrimaryFirst()
    {
        using var command = new RejectingCommand();
        var source = new ProbeEnumerable<int>(new[] { 1 }, failDispose: true);

        var exception = Assert.Throws<AggregateException>(() => InquiryTvpParameter.Bind(
            command, "@ids", source, "[dbo].[Inquiry_Tvp_test]",
            InquiryTvpDescriptor.Get("int", 0, 10, 0, false)));

        Assert.Collection(exception.InnerExceptions,
            static error => Assert.IsType<ParameterAddException>(error),
            static error => Assert.IsType<ProbeException>(error));
        Assert.Equal(1, source.DisposeCount);
    }

    [Fact]
    public void DescriptorCachePreservesExactMetadataIdentity()
    {
        var first = InquiryTvpDescriptor.Get("varchar", 37, 0, 0, false);
        var second = InquiryTvpDescriptor.Get("varchar", 37, 0, 0, false);
        Assert.Same(first, second);

        using var command = new SqlCommand();
        InquiryTvpParameter.Bind(command, "@codes", new[] { "abc" }, "[dbo].[Inquiry_Tvp_test]", first);
        var record = Assert.Single(Assert.IsAssignableFrom<IEnumerable<Microsoft.Data.SqlClient.Server.SqlDataRecord>>(
            Assert.IsType<SqlParameter>(Assert.Single(command.Parameters.Cast<SqlParameter>())).Value));
        var metadata = record.GetSqlMetaData(0);
        Assert.Equal(SqlDbType.VarChar, metadata.SqlDbType);
        Assert.Equal(37, metadata.MaxLength);
    }

    [Fact]
    public void CommandResourceDisposalAttemptsEverySourceAndAggregatesFailures()
    {
        using var command = new SqlCommand();
        var first = new ProbeEnumerable<int>(new[] { 1 }, failDispose: true);
        var second = new ProbeEnumerable<int>(new[] { 2 }, failDispose: true);
        var descriptor = InquiryTvpDescriptor.Get("int", 0, 10, 0, false);
        InquiryTvpParameter.Bind(command, "@first", first, "[dbo].[Inquiry_Tvp_test]", descriptor);
        InquiryTvpParameter.Bind(command, "@second", second, "[dbo].[Inquiry_Tvp_test]", descriptor);

        var exception = Assert.Throws<AggregateException>(() => InquiryCommandResources.Dispose(command));

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.All(exception.InnerExceptions, static error => Assert.IsType<ProbeException>(error));
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
    }

    private sealed class ProbeException : Exception { }

    private sealed class ProbeEnumerable<T> : IEnumerable<T>
    {
        private readonly IReadOnlyList<T> _values;
        private readonly int _failMoveNextAt;
        private readonly int _failCurrentAt;
        private readonly bool _failDispose;

        public ProbeEnumerable(IReadOnlyList<T> values, int failMoveNextAt = -1, int failCurrentAt = -1, bool failDispose = false)
        {
            _values = values;
            _failMoveNextAt = failMoveNextAt;
            _failCurrentAt = failCurrentAt;
            _failDispose = failDispose;
        }

        public int GetEnumeratorCount { get; private set; }
        public int MoveNextCount { get; private set; }
        public int CurrentCount { get; private set; }
        public int DisposeCount { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            GetEnumeratorCount++;
            return new Enumerator(this);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class Enumerator : IEnumerator<T>
        {
            private readonly ProbeEnumerable<T> _owner;
            private int _index = -1;
            private bool _disposed;

            public Enumerator(ProbeEnumerable<T> owner) => _owner = owner;

            public T Current
            {
                get
                {
                    _owner.CurrentCount++;
                    if (_owner.CurrentCount == _owner._failCurrentAt) throw new ProbeException();
                    return _owner._values[_index];
                }
            }

            object? System.Collections.IEnumerator.Current => Current;

            public bool MoveNext()
            {
                _owner.MoveNextCount++;
                if (_owner.MoveNextCount == _owner._failMoveNextAt) throw new ProbeException();
                _index++;
                return _index < _owner._values.Count;
            }

            public void Reset() => throw new NotSupportedException();

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _owner.DisposeCount++;
                if (_owner._failDispose) throw new ProbeException();
            }
        }
    }

    private sealed class ParameterAddException : Exception;

    private sealed class RejectingCommand : DbCommand
    {
        protected override DbParameterCollection DbParameterCollection { get; } = new RejectingParameterCollection();
        [System.Diagnostics.CodeAnalysis.AllowNull] public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbTransaction? DbTransaction { get; set; }
        public override void Cancel() { }
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        public override object? ExecuteScalar() => throw new NotSupportedException();
        public override void Prepare() => throw new NotSupportedException();
        protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
    }

    private sealed class RejectingParameterCollection : DbParameterCollection
    {
        public override int Count => 0;
        public override object SyncRoot => this;
        public override int Add(object value) => throw new ParameterAddException();
        public override void AddRange(Array values) => throw new ParameterAddException();
        public override void Clear() { }
        public override bool Contains(object value) => false;
        public override bool Contains(string value) => false;
        public override void CopyTo(Array array, int index) { }
        public override System.Collections.IEnumerator GetEnumerator() => Array.Empty<object>().GetEnumerator();
        public override int IndexOf(object value) => -1;
        public override int IndexOf(string parameterName) => -1;
        public override void Insert(int index, object value) => throw new ParameterAddException();
        public override void Remove(object value) { }
        public override void RemoveAt(int index) { }
        public override void RemoveAt(string parameterName) { }
        protected override DbParameter GetParameter(int index) => throw new IndexOutOfRangeException();
        protected override DbParameter GetParameter(string parameterName) => throw new IndexOutOfRangeException();
        protected override void SetParameter(int index, DbParameter value) => throw new ParameterAddException();
        protected override void SetParameter(string parameterName, DbParameter value) => throw new ParameterAddException();
    }
}
