using System.Collections;
using System.Reflection;

namespace Inquiry.Parameters;

internal static class InquiryParameterReader
{
    private static readonly Type[] ScalarTypes =
    {
        typeof(string),
        typeof(decimal),
        typeof(DateTime),
        typeof(DateTimeOffset),
        typeof(Guid),
        typeof(byte[]),
    };

    public static IReadOnlyList<InquiryParameter> Read(object? parameters)
    {
        if (parameters is null)
        {
            return Array.Empty<InquiryParameter>();
        }

        if (parameters is InquiryParameter parameter)
        {
            return new[] { parameter };
        }

        if (parameters is IEnumerable<InquiryParameter> inquiryParameters)
        {
            return inquiryParameters.ToArray();
        }

        if (parameters is IEnumerable<KeyValuePair<string, object?>> objectPairs)
        {
            return objectPairs
                .Select(static pair => new InquiryParameter(pair.Key, pair.Value))
                .ToArray();
        }

        if (parameters is IDictionary dictionary)
        {
            return ReadDictionary(dictionary);
        }

        if (parameters is IEnumerable enumerable && parameters is not string)
        {
            return ReadEnumerable(enumerable);
        }

        return ReadProperties(parameters);
    }

    private static IReadOnlyList<InquiryParameter> ReadDictionary(IDictionary dictionary)
    {
        var parameters = new List<InquiryParameter>(dictionary.Count);
        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is not string name)
            {
                throw new ArgumentException("Parameter dictionary keys must be strings.", nameof(dictionary));
            }

            parameters.Add(new InquiryParameter(name, entry.Value));
        }

        return parameters;
    }

    private static IReadOnlyList<InquiryParameter> ReadEnumerable(IEnumerable enumerable)
    {
        var parameters = new List<InquiryParameter>();
        foreach (var item in enumerable)
        {
            if (item is null)
            {
                throw new ArgumentException("Parameter collections cannot contain null entries.", nameof(enumerable));
            }

            if (item is InquiryParameter parameter)
            {
                parameters.Add(parameter);
                continue;
            }

            var itemType = item.GetType();
            if (itemType.IsGenericType && itemType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                var keyProperty = itemType.GetProperty("Key");
                var valueProperty = itemType.GetProperty("Value");
                var key = keyProperty?.GetValue(item);
                if (key is not string name)
                {
                    throw new ArgumentException("Parameter pair keys must be strings.", nameof(enumerable));
                }

                parameters.Add(new InquiryParameter(name, valueProperty?.GetValue(item)));
                continue;
            }

            throw new ArgumentException(
                "Parameter collections must contain InquiryParameter values or key/value pairs.",
                nameof(enumerable));
        }

        return parameters;
    }

    private static IReadOnlyList<InquiryParameter> ReadProperties(object parameters)
    {
        var type = parameters.GetType();
        if (IsScalar(type))
        {
            throw new ArgumentException(
                "Parameters must be supplied as an anonymous object, dictionary, or collection of InquiryParameter values.",
                nameof(parameters));
        }

        return type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static property => property.CanRead && property.GetIndexParameters().Length == 0)
            .Select(property => new InquiryParameter(property.Name, property.GetValue(parameters)))
            .ToArray();
    }

    private static bool IsScalar(Type type)
    {
        var nonNullableType = Nullable.GetUnderlyingType(type) ?? type;
        return nonNullableType.IsPrimitive ||
            nonNullableType.IsEnum ||
            ScalarTypes.Contains(nonNullableType);
    }
}
