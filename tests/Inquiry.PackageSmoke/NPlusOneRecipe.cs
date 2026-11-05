using System.Text;
using Inquiry.Commands;
using Inquiry.DependencyInjection;
using Inquiry.Generated;
using Inquiry.Interceptors;
using Inquiry.Sqlite.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Drives NPlusOneExample, Order and OrderStore, all extracted from
// docs/site/articles/features/interceptors.md, and checks every rule that page states.
const string connectionString = "Data Source=n-plus-one-recipe.db";
const string category = "Inquiry.Interceptors.NPlusOneDetectionInterceptor";
await using (var connection = new SqliteConnection(connectionString))
{
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = InquiryGeneratedSchema.Ddl;
    await command.ExecuteNonQueryAsync();
}

// The documented example. "C" is one executed command and "W" one N+1 warning, in order. The
// detector runs before the trace interceptor, so a warning appears just before its command.
var trace = new Trace();
await RunAsync(
    services => services.AddOrders(connectionString, logging => logging.AddProvider(trace)),
    trace,
    orders => NPlusOneExample.RunAsync(orders));
Expect("documented example", "CCCC" + "C" + "CCWCC" + "CWCC" + "CCCC", trace.Sequence);
if (trace.Warnings.Count != 2)
{
    throw new InvalidOperationException($"Expected two warnings, found {trace.Warnings.Count}.");
}

foreach (var warning in trace.Warnings)
{
    if (warning.Category != category || warning.Level != LogLevel.Warning
        || !warning.Message.StartsWith("Possible N+1: the same SQL executed 3 times within this detection scope: SELECT ", StringComparison.Ordinal)
        || !warning.Message.EndsWith("FROM \"Orders\" WHERE \"CustomerID\" = @CustomerID", StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Unexpected warning: {warning.Category} {warning.Level} {warning.Message}");
    }
}

Console.WriteLine(trace.Warnings[0].Message);

// The default threshold is 2, so the first repeat warns.
var defaults = new Trace();
await RunAsync(
    services => services.AddLogging(logging => logging.AddProvider(defaults)).AddInquiry().AddInquiryGeneratedStores()
        .AddInquirySqlite(connectionString).AddInquiryNPlusOneDetection(),
    defaults,
    async orders =>
    {
        using (InquiryNPlusOneScope.BeginScope())
        {
            await NPlusOneExample.LoadOrdersAsync(orders, [1, 2, 3]);
        }
    });
Expect("default threshold", "CWCC", defaults.Sequence);

// A scope begun inside an async helper does not cover the caller.
var helper = new Trace();
await RunAsync(
    services => services.AddOrders(connectionString, logging => logging.AddProvider(helper)),
    helper,
    async orders =>
    {
        using (await BeginInHelperAsync())
        {
            await NPlusOneExample.LoadOrdersAsync(orders, [1, 2, 3, 4]);
        }
    });
Expect("scope begun in an async helper", "CCCC", helper.Sequence);

// Without logging, resolving the interceptor fails.
await ExpectResolutionFailure<InvalidOperationException>(
    "missing logging",
    services => services.AddInquiry().AddInquiryGeneratedStores().AddInquirySqlite(connectionString).AddInquiryNPlusOneDetection(threshold: 3));

// A threshold below 2 fails when the interceptor is first resolved, not at registration.
await ExpectResolutionFailure<ArgumentOutOfRangeException>(
    "threshold 1",
    services => services.AddLogging().AddInquiry().AddInquiryGeneratedStores().AddInquirySqlite(connectionString).AddInquiryNPlusOneDetection(threshold: 1));

Console.WriteLine("N-PLUS-ONE-RECIPE-OK");

static async Task RunAsync(Action<IServiceCollection> register, Trace trace, Func<OrderStore, Task> work)
{
    var services = new ServiceCollection();
    register(services);
    services.AddSingleton<IInquiryCommandInterceptor>(trace);
    await using var provider = services.BuildServiceProvider(
        new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    await using var scope = provider.CreateAsyncScope();
    await work(scope.ServiceProvider.GetRequiredService<OrderStore>());
}

static async Task ExpectResolutionFailure<TException>(string name, Action<IServiceCollection> register)
    where TException : Exception
{
    var services = new ServiceCollection();
    register(services);

    // Building succeeds: the failure surfaces only when the interceptor is resolved.
    await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    await using var scope = provider.CreateAsyncScope();
    try
    {
        var orders = scope.ServiceProvider.GetRequiredService<OrderStore>();
        await orders.ForCustomerAsync(1);
    }
    catch (TException exception)
    {
        Console.WriteLine($"{name}: {exception.GetType().Name}");
        return;
    }

    throw new InvalidOperationException($"{name}: expected {typeof(TException).Name}.");
}

static async Task<InquiryNPlusOneScope> BeginInHelperAsync()
{
    await Task.Yield();
    return InquiryNPlusOneScope.BeginScope();
}

static void Expect(string name, string expected, string actual)
{
    if (expected != actual)
    {
        throw new InvalidOperationException($"{name}: expected {expected}, got {actual}.");
    }
}

internal sealed record LogEntry(string Category, LogLevel Level, string Message);

internal sealed class Trace : ILoggerProvider, IInquiryCommandInterceptor
{
    private readonly StringBuilder _sequence = new();

    public List<LogEntry> Warnings { get; } = [];

    public string Sequence
    {
        get
        {
            lock (_sequence)
            {
                return _sequence.ToString();
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public ValueTask CommandExecutingAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
    {
        Append('C');
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
    }

    private void Append(char entry)
    {
        lock (_sequence)
        {
            _sequence.Append(entry);
        }
    }

    private sealed class Logger(Trace trace, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Warning)
            {
                return;
            }

            lock (trace._sequence)
            {
                trace._sequence.Append('W');
                trace.Warnings.Add(new LogEntry(category, logLevel, formatter(state, exception)));
            }
        }
    }
}
