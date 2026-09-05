namespace Inquiry;

/// <summary>
/// Runtime configuration for Inquiry, supplied via the
/// <see cref="DependencyInjection.InquiryServiceCollectionExtensions.AddInquiry(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action{InquiryOptions})"/>
/// overload.
/// </summary>
public sealed class InquiryOptions
{
    /// <summary>
    /// Default maximum number of parameters Inquiry will bind into a single generated command.
    /// </summary>
    public const int DefaultMaxParametersPerCommand = 2000;

    /// <summary>Default maximum number of items retained and executed in one batch chunk.</summary>
    public const int DefaultMaxBatchSize = 1000;

    private TimeSpan? _defaultCommandTimeout;
    private int _maxBatchSize = DefaultMaxBatchSize;
    private int _maxParametersPerCommand = DefaultMaxParametersPerCommand;

    /// <summary>
    /// Gets or sets the command timeout applied to every command Inquiry executes, unless an
    /// <see cref="Commands.InquiryCommand"/> carries its own explicit timeout. Defaults to
    /// <see langword="null"/>, leaving the ADO.NET provider's default (typically 30 seconds) in
    /// effect. Sub-second values round up to one second (<see cref="System.Data.Common.DbCommand.CommandTimeout"/>
    /// has whole-second granularity).
    /// </summary>
    public TimeSpan? DefaultCommandTimeout
    {
        get => _defaultCommandTimeout;
        set
        {
            if (value is { } timeout && (timeout <= TimeSpan.Zero || timeout.TotalSeconds > int.MaxValue))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Default command timeout must be positive and at most int.MaxValue seconds.");
            }

            _defaultCommandTimeout = value;
        }
    }

    /// <summary>
    /// Gets or sets whether generated commands are prepared before execution. Defaults to
    /// <see cref="PreparedStatementMode.Auto"/>.
    /// </summary>
    public PreparedStatementMode PrepareStatements { get; set; } = PreparedStatementMode.Auto;

    /// <summary>
    /// Gets or sets whether a 0-row UPDATE/DELETE on an optimistic-concurrency token entity
    /// throws <see cref="InquiryConcurrencyException"/> instead of returning <see langword="false"/>
    /// (or a <see langword="null"/> entity result). Defaults to <see langword="false"/>,
    /// preserving the backward-compatible "not found" contract.
    /// </summary>
    public bool ThrowOnConcurrencyConflict { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of parameters Inquiry will bind into one generated command.
    /// This bounds <c>Compare.In</c>, batch insert, and batch update expansion before a
    /// provider-specific parameter cap is hit. Defaults to <see cref="DefaultMaxParametersPerCommand"/>.
    /// </summary>
    public int MaxParametersPerCommand
    {
        get => _maxParametersPerCommand;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Maximum parameters per command must be positive.");
            }

            _maxParametersPerCommand = value;
        }
    }

    /// <summary>
    /// Gets or sets the maximum number of items Inquiry retains and executes in one batch chunk.
    /// Defaults to <see cref="DefaultMaxBatchSize"/>.
    /// </summary>
    public int MaxBatchSize
    {
        get => _maxBatchSize;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Maximum batch size must be positive.");
            }

            _maxBatchSize = value;
        }
    }
}
