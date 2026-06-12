using System.Data.Common;

namespace Inquiry.Commands;

/// <summary>
/// Generated-code support surface: a uniform parameter-binding target that wraps either a
/// <see cref="DbCommand"/> (sequential execution) or a <see cref="DbBatchCommand"/> (DbBatch
/// execution). Generated batch binders write parameters through this struct so the same static
/// delegate works on both execution paths without knowing which one the pipeline chose.
/// </summary>
/// <remarks>
/// Exactly one of the two wrapped references is non-null; the pipeline constructs the struct,
/// so user code never observes an empty target. It is a readonly struct (no allocation per item)
/// and is only ever constructed by the built-in pipelines and the compatibility default
/// implementations of <c>ExecuteBatchAsync</c>.
/// </remarks>
public readonly struct InquiryParameterTarget
{
    private readonly DbCommand? _command;
    private readonly DbBatchCommand? _batchCommand;

    /// <summary>
    /// Initializes a target that binds parameters onto a <see cref="DbCommand"/>.
    /// </summary>
    internal InquiryParameterTarget(DbCommand command)
    {
        _command = command;
        _batchCommand = null;
    }

    /// <summary>
    /// Initializes a target that binds parameters onto a <see cref="DbBatchCommand"/>.
    /// </summary>
    internal InquiryParameterTarget(DbBatchCommand batchCommand)
    {
        _command = null;
        _batchCommand = batchCommand;
    }

    /// <summary>
    /// Creates a provider-specific parameter via the wrapped command —
    /// <see cref="DbCommand.CreateParameter"/> or <see cref="DbBatchCommand.CreateParameter"/>.
    /// The parameter is not added until <see cref="AddParameter"/> is called.
    /// </summary>
    public DbParameter CreateParameter()
        => _command is not null ? _command.CreateParameter() : _batchCommand!.CreateParameter();

    /// <summary>
    /// Adds <paramref name="parameter"/> to the wrapped command's parameter collection.
    /// </summary>
    public void AddParameter(DbParameter parameter)
    {
        if (_command is not null)
        {
            _command.Parameters.Add(parameter);
        }
        else
        {
            _batchCommand!.Parameters.Add(parameter);
        }
    }
}
