using System.Data;

namespace Inquiry.Parameters;

/// <summary>
/// Describes a database parameter that can be bound to an Inquiry command.
/// </summary>
/// <remarks>
/// A <c>readonly struct</c> rather than a class so the generator's per-call
/// <c>new InquiryParameter("Foo", entity.Foo)</c> calls do not allocate. The single boxing
/// cost is the value-type column itself flowing through the <see cref="Value"/> field, which
/// happens regardless of how this type is shaped.
/// </remarks>
public readonly struct InquiryParameter : IEquatable<InquiryParameter>
{
    /// <summary>
    /// Initializes a new <see cref="InquiryParameter"/>.
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

    /// <summary>Gets the parameter value.</summary>
    public object? Value { get; }

    /// <summary>Gets the optional database type.</summary>
    public DbType? DbType { get; }

    /// <summary>Gets the optional parameter direction.</summary>
    public ParameterDirection? Direction { get; }

    /// <summary>Gets the optional parameter size.</summary>
    public int? Size { get; }

    /// <summary>Gets the optional parameter precision.</summary>
    public byte? Precision { get; }

    /// <summary>Gets the optional parameter scale.</summary>
    public byte? Scale { get; }

    /// <inheritdoc />
    public bool Equals(InquiryParameter other)
        => string.Equals(Name, other.Name, StringComparison.Ordinal)
        && Equals(Value, other.Value)
        && DbType == other.DbType
        && Direction == other.Direction
        && Size == other.Size
        && Precision == other.Precision
        && Scale == other.Scale;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is InquiryParameter other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Name?.GetHashCode() ?? 0;

    /// <summary>Equality operator.</summary>
    public static bool operator ==(InquiryParameter left, InquiryParameter right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(InquiryParameter left, InquiryParameter right) => !left.Equals(right);
}
