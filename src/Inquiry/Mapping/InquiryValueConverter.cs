using System.Globalization;

namespace Inquiry;

internal static class InquiryValueConverter
{
    public static object? FromDatabaseValue(object? value, Type targetType)
    {
        if (value is null || value is DBNull)
        {
            return IsNullable(targetType) ? null : Activator.CreateInstance(targetType);
        }

        var nullableType = Nullable.GetUnderlyingType(targetType);
        var effectiveType = nullableType ?? targetType;

        if (effectiveType.IsInstanceOfType(value))
        {
            return value;
        }

        if (effectiveType.IsEnum)
        {
            if (value is string name)
            {
                return Enum.Parse(effectiveType, name, ignoreCase: true);
            }

            return Enum.ToObject(effectiveType, value);
        }

        if (effectiveType == typeof(Guid))
        {
            return value is Guid guid ? guid : Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!);
        }

        if (effectiveType == typeof(DateTimeOffset))
        {
            if (value is DateTimeOffset dateTimeOffset)
            {
                return dateTimeOffset;
            }

            if (value is DateTime dateTime)
            {
                return new DateTimeOffset(dateTime);
            }

            return DateTimeOffset.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        }

        if (effectiveType == typeof(TimeSpan))
        {
            return value is TimeSpan timeSpan
                ? timeSpan
                : TimeSpan.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        }

        return Convert.ChangeType(value, effectiveType, CultureInfo.InvariantCulture);
    }

    private static bool IsNullable(Type type)
    {
        return !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
    }
}

public static class InquiryTypeConversion
{
    public static object? FromDatabaseValue(object? value, Type targetType)
    {
        return InquiryValueConverter.FromDatabaseValue(value, targetType);
    }
}
