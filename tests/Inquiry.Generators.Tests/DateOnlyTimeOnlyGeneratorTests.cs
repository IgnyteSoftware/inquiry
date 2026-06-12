using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// First-class <see cref="DateOnly"/>/<see cref="TimeOnly"/> support: materializers read via
/// <c>GetFieldValue&lt;T&gt;</c> (DbDataReader has no GetDateOnly/GetTimeOnly), binders stamp
/// <c>DbType.Date</c>/<c>DbType.Time</c>, and DDL emits the per-dialect date/time column types.
/// Nullable variants flow like <c>Guid?</c> (IsDBNull guard + nullable cast).
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string DateTimeOnlySource = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Event")]
        public sealed class Event
        {
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }

            [InquiryColumn("EventDate")]
            public DateOnly EventDate { get; set; }

            [InquiryColumn("StartTime")]
            public TimeOnly StartTime { get; set; }

            [InquiryColumn("EndDate")]
            public DateOnly? EndDate { get; set; }

            [InquiryColumn("EndTime")]
            public TimeOnly? EndTime { get; set; }
        }

        public partial class EventStore : Inquiry.Stores.InquiryStore<Demo.Event>
        {
            [InquiryInsert]
            public partial Task<int> InsertAsync(Event item, CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void DateOnlyTimeOnlyColumnsReadViaGetFieldValue()
    {
        var result = RunGenerator(DateTimeOnlySource);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("Event.InquiryEntity.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("EventDate = reader.GetFieldValue<global::System.DateOnly>(1)", text);
        Assert.Contains("StartTime = reader.GetFieldValue<global::System.TimeOnly>(2)", text);
        // Nullable variants get the IsDBNull guard with the nullable cast, mirroring Guid?.
        Assert.Contains("EndDate = reader.IsDBNull(3) ? (global::System.DateOnly?)null : reader.GetFieldValue<global::System.DateOnly>(3)", text);
        Assert.Contains("EndTime = reader.IsDBNull(4) ? (global::System.TimeOnly?)null : reader.GetFieldValue<global::System.TimeOnly>(4)", text);
    }

    [Fact]
    public void DateOnlyTimeOnlyParametersStampDateAndTimeDbTypes()
    {
        var result = RunGenerator(DateTimeOnlySource);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("EventStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // Trailing ';' so DbType.Date cannot false-match the DbType.DateTime2 stamp.
        Assert.Contains("DbType = global::System.Data.DbType.Date;", text);
        Assert.Contains("DbType = global::System.Data.DbType.Time;", text);
    }

    [Fact]
    public void SqliteSchemaMapsDateOnlyTimeOnlyToText()
    {
        var result = RunGenerator(DateTimeOnlySource);
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);

        Assert.Contains("\"EventDate\" TEXT NOT NULL", ddl);
        Assert.Contains("\"StartTime\" TEXT NOT NULL", ddl);
        // Nullable variants drop NOT NULL.
        Assert.Contains("\"EndDate\" TEXT,", ddl);
        Assert.Contains("\"EndTime\" TEXT", ddl);
        Assert.DoesNotContain("\"EndTime\" TEXT NOT NULL", ddl);
    }

    [Fact]
    public void SqlServerSchemaMapsDateOnlyTimeOnlyToDateAndTime()
    {
        var result = RunGenerator(DateTimeOnlySource, dialect: "SqlServer");
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);

        Assert.Contains("[EventDate] DATE NOT NULL", ddl);
        Assert.Contains("[StartTime] TIME NOT NULL", ddl);
        Assert.Contains("[EndDate] DATE,", ddl);
        Assert.Contains("[EndTime] TIME", ddl);
        Assert.DoesNotContain("[EndTime] TIME NOT NULL", ddl);
    }

    [Fact]
    public void PostgreSqlSchemaMapsDateOnlyTimeOnlyToDateAndTime()
    {
        var result = RunGenerator(DateTimeOnlySource, dialect: "PostgreSql");
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);

        Assert.Contains("\"EventDate\" DATE NOT NULL", ddl);
        Assert.Contains("\"StartTime\" TIME NOT NULL", ddl);
        Assert.Contains("\"EndDate\" DATE,", ddl);
    }

    [Fact]
    public void MySqlSchemaMapsDateOnlyTimeOnlyToDateAndTime6()
    {
        var result = RunGenerator(DateTimeOnlySource, dialect: "MySql");
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);

        Assert.Contains("`EventDate` DATE NOT NULL", ddl);
        Assert.Contains("`StartTime` TIME(6) NOT NULL", ddl);
        Assert.Contains("`EndDate` DATE,", ddl);
    }

    [Fact]
    public void OracleSchemaMapsDateOnlyToDateAndTimeOnlyToDayToSecondInterval()
    {
        var result = RunGenerator(DateTimeOnlySource, dialect: "Oracle");
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);

        Assert.Contains("EventDate DATE NOT NULL", ddl);
        Assert.Contains("StartTime INTERVAL DAY(0) TO SECOND(7) NOT NULL", ddl);
        Assert.Contains("EndDate DATE,", ddl);
    }
}
