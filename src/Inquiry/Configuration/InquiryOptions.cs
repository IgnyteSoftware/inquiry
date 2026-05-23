using System.Data.Common;
using System.Text;

namespace Inquiry;

public sealed class InquiryOptions
{
    private Func<IServiceProvider?, CancellationToken, ValueTask<DbConnection>>? _connectionFactory;

    public IInquiryProvider? Provider { get; private set; }

    public InquiryLoggingOptions Logging { get; } = new();

    public InquiryTelemetryOptions Telemetry { get; } = new();

    public InquiryConventionOptions Conventions { get; } = new();

    public InquiryPipelineOptions Pipeline { get; } = new();

    public InquiryPerformanceOptions Performance { get; } = new();

    public bool OwnsConnections { get; private set; } = true;

    public bool GeneratedMappingsEnabled { get; private set; }

    public InquiryOptions UseProvider(IInquiryProvider provider)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        return this;
    }

    public InquiryOptions UseConnection(DbConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connectionFactory = (_, _) => ValueTask.FromResult(connection);
        OwnsConnections = false;
        return this;
    }

    public InquiryOptions UseConnectionFactory(Func<DbConnection> connectionFactory, bool ownsConnection = true)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = (_, _) => ValueTask.FromResult(connectionFactory());
        OwnsConnections = ownsConnection;
        return this;
    }

    public InquiryOptions UseConnectionFactory(
        Func<IServiceProvider?, CancellationToken, ValueTask<DbConnection>> connectionFactory,
        bool ownsConnection = true)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        OwnsConnections = ownsConnection;
        return this;
    }

    public InquiryOptions UseMiddleware<TMiddleware>()
        where TMiddleware : IInquiryMiddleware
    {
        Pipeline.UseMiddleware<TMiddleware>();
        return this;
    }

    public InquiryOptions UseMiddleware(IInquiryMiddleware middleware)
    {
        Pipeline.UseMiddleware(middleware);
        return this;
    }

    public InquiryOptions UseOpenTelemetry()
    {
        Telemetry.Enabled = true;
        UseMiddleware<OpenTelemetryInquiryMiddleware>();
        return this;
    }

    public InquiryOptions UseGeneratedMappings()
    {
        GeneratedMappingsEnabled = true;
        return this;
    }

    public Func<IServiceProvider?, CancellationToken, ValueTask<DbConnection>> GetConnectionFactory()
    {
        if (_connectionFactory is null)
        {
            throw new InquiryValidationException(
                "Inquiry requires a DbConnection or connection factory. Configure one with UseConnection, UseConnectionFactory, or a provider-specific overload.");
        }

        return _connectionFactory;
    }
}

public sealed class InquiryLoggingOptions
{
    public bool EnableCommandLogging { get; set; }

    public bool EnableParameterLogging { get; set; }

    public bool EnableSensitiveDataLogging { get; set; }

    public TimeSpan? SlowQueryThreshold { get; set; } = TimeSpan.FromMilliseconds(500);
}

public sealed class InquiryTelemetryOptions
{
    public bool Enabled { get; set; }

    public bool IncludeSqlText { get; set; }

    public bool IncludeParameterValues { get; set; }
}

public enum InquiryNamingConvention
{
    Exact,
    CamelCase,
    PascalCase,
    SnakeCase,
    KebabCase,
    LowerCase,
    UpperCase,
    Custom
}

public sealed class InquiryConventionOptions
{
    public InquiryNamingConvention TableNaming { get; set; } = InquiryNamingConvention.Exact;

    public InquiryNamingConvention ColumnNaming { get; set; } = InquiryNamingConvention.Exact;

    public string? DefaultSchema { get; set; }

    public bool AllowUnattributedEntities { get; set; }

    public Func<string, string>? CustomTableNameConverter { get; set; }

    public Func<string, string>? CustomColumnNameConverter { get; set; }

    public string ConvertTableName(string name)
    {
        return ConvertName(name, TableNaming, CustomTableNameConverter);
    }

    public string ConvertColumnName(string name)
    {
        return ConvertName(name, ColumnNaming, CustomColumnNameConverter);
    }

    private static string ConvertName(string name, InquiryNamingConvention convention, Func<string, string>? custom)
    {
        return convention switch
        {
            InquiryNamingConvention.Exact => name,
            InquiryNamingConvention.CamelCase => string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..],
            InquiryNamingConvention.PascalCase => string.IsNullOrEmpty(name) ? name : char.ToUpperInvariant(name[0]) + name[1..],
            InquiryNamingConvention.SnakeCase => SplitWords(name, "_").ToLowerInvariant(),
            InquiryNamingConvention.KebabCase => SplitWords(name, "-").ToLowerInvariant(),
            InquiryNamingConvention.LowerCase => name.ToLowerInvariant(),
            InquiryNamingConvention.UpperCase => name.ToUpperInvariant(),
            InquiryNamingConvention.Custom => custom?.Invoke(name) ?? throw new InquiryValidationException("A custom naming convention requires a converter delegate."),
            _ => name
        };
    }

    private static string SplitWords(string name, string separator)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var builder = new StringBuilder(name.Length + 8);
        for (var index = 0; index < name.Length; index++)
        {
            var current = name[index];
            if (char.IsUpper(current) && index > 0 && (char.IsLower(name[index - 1]) || index + 1 < name.Length && char.IsLower(name[index + 1])))
            {
                builder.Append(separator);
            }

            builder.Append(current);
        }

        return builder.ToString();
    }
}

public sealed class InquiryPerformanceOptions
{
    public bool CacheGeneratedSql { get; set; } = true;
}
