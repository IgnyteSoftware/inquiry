namespace Inquiry;

/// <summary>
/// Maps an entity type to a relational database table.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class InquiryTableAttribute : Attribute
{
    public InquiryTableAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Table name cannot be empty.", nameof(name));
        }

        Name = name;
    }

    public string Name { get; }

    public string? Schema { get; init; }
}

/// <summary>
/// Maps an entity property to a database column.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InquiryColumnAttribute : Attribute
{
    public InquiryColumnAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Column name cannot be empty.", nameof(name));
        }

        Name = name;
    }

    public string Name { get; }

    public bool IsRequired { get; init; }
}

/// <summary>
/// Marks a property as part of the primary key.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InquiryKeyAttribute : Attribute
{
    public int Order { get; init; }

    public bool DatabaseGenerated { get; init; }
}

/// <summary>
/// Excludes a property from Inquiry mapping.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InquiryIgnoreAttribute : Attribute
{
}

/// <summary>
/// Marks a property as an optimistic concurrency token.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InquiryConcurrencyTokenAttribute : Attribute
{
}

/// <summary>
/// Marks a property as a creation timestamp.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InquiryCreatedAtAttribute : Attribute
{
}

/// <summary>
/// Marks a property as an update timestamp.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InquiryUpdatedAtAttribute : Attribute
{
}

/// <summary>
/// Excludes a property from generated insert commands.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InquiryInsertIgnoreAttribute : Attribute
{
}

/// <summary>
/// Excludes a property from generated update commands.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InquiryUpdateIgnoreAttribute : Attribute
{
}

/// <summary>
/// Marks a property as database read-only.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InquiryReadOnlyAttribute : Attribute
{
}

/// <summary>
/// Marks a property as database-computed.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InquiryComputedAttribute : Attribute
{
}

/// <summary>
/// Documents a database default value.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InquiryDefaultValueAttribute : Attribute
{
    public InquiryDefaultValueAttribute(object? value)
    {
        Value = value;
    }

    public object? Value { get; }
}

/// <summary>
/// Documents the maximum length of a mapped property.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InquiryMaxLengthAttribute : Attribute
{
    public InquiryMaxLengthAttribute(int length)
    {
        if (length < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be greater than zero.");
        }

        Length = length;
    }

    public int Length { get; }
}

/// <summary>
/// Documents precision and scale for decimal values.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InquiryPrecisionAttribute : Attribute
{
    public InquiryPrecisionAttribute(int precision, int scale = 0)
    {
        if (precision < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(precision), "Precision must be greater than zero.");
        }

        if (scale < 0 || scale > precision)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), "Scale must be between zero and precision.");
        }

        Precision = precision;
        Scale = scale;
    }

    public int Precision { get; }

    public int Scale { get; }
}

/// <summary>
/// Marks a boolean property used by soft-delete conventions.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InquirySoftDeleteAttribute : Attribute
{
}

/// <summary>
/// Marks a property used by multi-tenant filters.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InquiryTenantIdAttribute : Attribute
{
}

/// <summary>
/// Associates a mapped property with a custom value converter.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InquiryConverterAttribute : Attribute
{
    public InquiryConverterAttribute(Type converterType)
    {
        ConverterType = converterType ?? throw new ArgumentNullException(nameof(converterType));
    }

    public Type ConverterType { get; }
}
