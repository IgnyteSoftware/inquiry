using System.Data;

namespace Inquiry;

/// <summary>
/// Describes a database parameter that can be bound to an Inquiry command.
/// </summary>
public sealed class InquiryParameter
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryParameter"/> class.
    /// </summary>
    public InquiryParameter(
        string name,
        object? value,
        DbType? dbType = null,
        ParameterDirection? direction = null,
        int? size = null,
        byte? precision = null,
        byte? scale = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Parameter name cannot be empty.", nameof(name));
        }

        Name = name;
        Value = value;
        DbType = dbType;
        Direction = direction;
        Size = size;
        Precision = precision;
        Scale = scale;
    }

    /// <summary>
    /// Gets the parameter name. Names without a provider prefix are bound with an <c>@</c> prefix.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the parameter value.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Gets the optional database type.
    /// </summary>
    public DbType? DbType { get; }

    /// <summary>
    /// Gets the optional parameter direction.
    /// </summary>
    public ParameterDirection? Direction { get; }

    /// <summary>
    /// Gets the optional parameter size.
    /// </summary>
    public int? Size { get; }

    /// <summary>
    /// Gets the optional parameter precision.
    /// </summary>
    public byte? Precision { get; }

    /// <summary>
    /// Gets the optional parameter scale.
    /// </summary>
    public byte? Scale { get; }
}
