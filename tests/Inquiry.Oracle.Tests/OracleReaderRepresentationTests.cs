using Inquiry.Oracle.Tests.Fixtures;
using Oracle.ManagedDataAccess.Client;

namespace Inquiry.Oracle.Tests;

[Collection(OracleCollection.Name)]
public sealed class OracleReaderRepresentationTests
{
    private readonly OracleContainerFixture _fixture;
    public OracleReaderRepresentationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task CharacterizesProviderFieldTypes()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var connection = new OracleConnection(_fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CAST(1 AS NUMBER(3)), CAST(2 AS NUMBER(5)), CAST(3 AS NUMBER(10)),
                   CAST(4 AS NUMBER(19)), CAST(1 AS NUMBER(1)), CAST(1.25 AS NUMBER(10,2)),
                   CAST(1 AS BINARY_FLOAT), CAST(1 AS BINARY_DOUBLE), SYS_GUID(), HEXTORAW('0102'),
                   CAST(SYSDATE AS DATE), SYSTIMESTAMP, COUNT(*)
            FROM dual
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var actual = Enumerable.Range(0, reader.FieldCount)
            .Select(i => reader.GetFieldType(i).FullName!)
            .ToArray();
        Assert.Equal(
        [
            "System.Int16", "System.Int32", "System.Int64", "System.Decimal", "System.Int16",
            "System.Double", "System.Single", "System.Double", "System.Byte[]", "System.Byte[]",
            "System.DateTime", "System.DateTimeOffset", "System.Decimal",
        ], actual);

        Assert.Equal((short)1, reader.GetInt16(0));
        Assert.Equal(2, reader.GetInt32(1));
        Assert.Equal(3L, reader.GetInt64(2));
        Assert.Equal(4m, reader.GetDecimal(3));
        Assert.True(reader.GetBoolean(4));
        Assert.Equal(1.25m, reader.GetDecimal(5));
        Assert.Equal(1f, reader.GetFloat(6));
        Assert.Equal(1d, reader.GetDouble(7));
        Assert.NotEqual(Guid.Empty, reader.GetGuid(8));
        Assert.Equal(new byte[] { 1, 2 }, reader.GetFieldValue<byte[]>(9));
        _ = reader.GetDateTime(10);
        _ = reader.GetFieldValue<DateTimeOffset>(11);
        Assert.Equal(1L, reader.GetInt64(12));
    }
}
