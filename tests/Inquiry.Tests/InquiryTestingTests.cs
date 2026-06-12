using Inquiry.Commands;
using Inquiry.Interceptors;
using Inquiry.Parameters;
using Inquiry.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Tests;

public sealed class InquiryTestingTests
{
    [Fact]
    public async Task FixtureCreatesWorkingPipeline()
    {
        await using var fixture = await SqliteInquiryFixture.CreateAsync();
        await fixture.ExecuteDdlAsync("CREATE TABLE Items (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);");

        using var scope = fixture.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();

        var affected = await inquiry.ExecuteAsync(new InquiryCommand(
            "INSERT INTO Items (Id, Name) VALUES (@Id, @Name)",
            new[]
            {
                new InquiryParameter("Id", 1),
                new InquiryParameter("Name", "Alpha"),
            }));
        Assert.Equal(1, affected);

        var name = await inquiry.ExecuteScalarAsync<string>(new InquiryCommand(
            "SELECT Name FROM Items WHERE Id = @Id",
            new[] { new InquiryParameter("Id", 1) }));
        Assert.Equal("Alpha", name);
    }

    [Fact]
    public async Task FixturesAreIsolatedFromEachOther()
    {
        await using var first = await SqliteInquiryFixture.CreateAsync();
        await using var second = await SqliteInquiryFixture.CreateAsync();

        Assert.NotEqual(first.ConnectionString, second.ConnectionString);

        await first.ExecuteDdlAsync("CREATE TABLE Items (Id INTEGER PRIMARY KEY);");

        using var scope = second.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        await Assert.ThrowsAsync<SqliteException>(
            () => inquiry.ExecuteScalarAsync<long>(new InquiryCommand("SELECT COUNT(*) FROM Items")));
    }

    [Fact]
    public async Task RecordingInterceptorCapturesTextAndParameters()
    {
        var recorder = new RecordingCommandInterceptor();
        await using var fixture = await SqliteInquiryFixture.CreateAsync(
            services => services.AddSingleton<IInquiryCommandInterceptor>(recorder));
        await fixture.ExecuteDdlAsync("CREATE TABLE Items (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);");

        using var scope = fixture.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        await inquiry.ExecuteAsync(new InquiryCommand(
            "INSERT INTO Items (Id, Name) VALUES (@Id, @Name)",
            new[]
            {
                new InquiryParameter("Id", 1),
                new InquiryParameter("Name", "Alpha"),
            }));

        var recorded = Assert.Single(recorder.Commands);
        Assert.Equal("INSERT INTO Items (Id, Name) VALUES (@Id, @Name)", recorded.CommandText);
        Assert.Equal(1, recorded.RecordsAffected);
        Assert.Null(recorded.Exception);
        Assert.Equal(2, recorded.Parameters.Count);
        Assert.Contains(recorded.Parameters, p => p.Name.EndsWith("Id", StringComparison.Ordinal) && Equals(p.Value, 1));
        Assert.Contains(recorded.Parameters, p => p.Name.EndsWith("Name", StringComparison.Ordinal) && Equals(p.Value, "Alpha"));

        recorder.Clear();
        Assert.Empty(recorder.Commands);
    }

    [Fact]
    public async Task RecordingInterceptorCapturesFailures()
    {
        var recorder = new RecordingCommandInterceptor();
        await using var fixture = await SqliteInquiryFixture.CreateAsync(
            services => services.AddSingleton<IInquiryCommandInterceptor>(recorder));

        using var scope = fixture.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        await Assert.ThrowsAsync<SqliteException>(
            () => inquiry.ExecuteAsync(new InquiryCommand("INSERT INTO NoSuchTable (Id) VALUES (1)")));

        var recorded = Assert.Single(recorder.Commands);
        Assert.IsType<SqliteException>(recorded.Exception);
        Assert.Null(recorded.RecordsAffected);
    }

    [Fact]
    public async Task AssertExecutedFindsMatchingCommandAndThrowsOtherwise()
    {
        var recorder = new RecordingCommandInterceptor();
        await using var fixture = await SqliteInquiryFixture.CreateAsync(
            services => services.AddSingleton<IInquiryCommandInterceptor>(recorder));
        await fixture.ExecuteDdlAsync("CREATE TABLE Items (Id INTEGER PRIMARY KEY);");

        using var scope = fixture.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        await inquiry.ExecuteAsync(new InquiryCommand("INSERT INTO Items (Id) VALUES (1)"));

        var recorded = recorder.AssertExecuted("INSERT INTO Items");
        Assert.Equal(1, recorded.RecordsAffected);

        recorder.AssertNotExecuted("DELETE FROM Items");

        var missing = Assert.Throws<InvalidOperationException>(() => recorder.AssertExecuted("DELETE FROM Items"));
        Assert.Contains("DELETE FROM Items", missing.Message);
        Assert.Contains("INSERT INTO Items (Id) VALUES (1)", missing.Message);

        var unexpected = Assert.Throws<InvalidOperationException>(() => recorder.AssertNotExecuted("INSERT INTO Items"));
        Assert.Contains("INSERT INTO Items", unexpected.Message);
    }
}
