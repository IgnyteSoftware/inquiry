using System.Collections;
using System.Reflection;

namespace Inquiry;

internal static class InquiryParameterReader
{
    public static IReadOnlyDictionary<string, object?> Read(object? parameters)
    {
        if (parameters is null)
        {
            return EmptyDictionary.Instance;
        }

        if (parameters is IReadOnlyDictionary<string, object?> readOnly)
        {
            return readOnly.ToDictionary(
                pair => pair.Key,
                pair => pair.Value is InquiryParameter parameter ? parameter.Value : pair.Value,
                StringComparer.OrdinalIgnoreCase);
        }

        if (parameters is IDictionary<string, object?> dictionary)
        {
            return dictionary.ToDictionary(
                pair => pair.Key,
                pair => pair.Value is InquiryParameter parameter ? parameter.Value : pair.Value,
                StringComparer.OrdinalIgnoreCase);
        }

        if (parameters is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            return pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        if (parameters is IDictionary legacyDictionary)
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in legacyDictionary)
            {
                if (entry.Key is not string key)
                {
                    throw new InquiryValidationException("Raw SQL parameter dictionaries must use string keys.");
                }

                result[key] = entry.Value is InquiryParameter parameter ? parameter.Value : entry.Value;
            }

            return result;
        }

        return parameters
            .GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0 && property.GetMethod is not null)
            .ToDictionary(
                property => property.Name,
                property =>
                {
                    var value = property.GetValue(parameters);
                    return value is InquiryParameter parameter ? parameter.Value : value;
                },
                StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<InquiryParameter> ReadCommandParameters(object? parameters)
    {
        if (parameters is null)
        {
            return Array.Empty<InquiryParameter>();
        }

        if (parameters is InquiryParameter parameter)
        {
            return new[] { parameter };
        }

        if (parameters is IReadOnlyList<InquiryParameter> parameterList)
        {
            return parameterList;
        }

        if (parameters is IEnumerable<InquiryParameter> parameterEnumerable)
        {
            return parameterEnumerable.ToArray();
        }

        if (parameters is IReadOnlyDictionary<string, object?> readOnly)
        {
            return readOnly
                .Select(pair => pair.Value is InquiryParameter parameterValue
                    ? parameterValue
                    : InquiryParameter.Input(pair.Key, pair.Value))
                .ToArray();
        }

        if (parameters is IDictionary<string, object?> dictionary)
        {
            return dictionary
                .Select(pair => pair.Value is InquiryParameter parameterValue
                    ? parameterValue
                    : InquiryParameter.Input(pair.Key, pair.Value))
                .ToArray();
        }

        if (parameters is IDictionary legacyDictionary)
        {
            var result = new List<InquiryParameter>();
            foreach (DictionaryEntry entry in legacyDictionary)
            {
                if (entry.Key is not string key)
                {
                    throw new InquiryValidationException("Command parameter dictionaries must use string keys.");
                }

                result.Add(entry.Value is InquiryParameter parameterValue
                    ? parameterValue
                    : InquiryParameter.Input(key, entry.Value));
            }

            return result;
        }

        return parameters
            .GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0 && property.GetMethod is not null)
            .Select(property =>
            {
                var value = property.GetValue(parameters);
                return value is InquiryParameter parameterValue
                    ? parameterValue
                    : InquiryParameter.Input(property.Name, value);
            })
            .ToArray();
    }

    private sealed class EmptyDictionary : IReadOnlyDictionary<string, object?>
    {
        public static readonly EmptyDictionary Instance = new();

        private EmptyDictionary()
        {
        }

        public int Count => 0;

        public IEnumerable<string> Keys => Array.Empty<string>();

        public IEnumerable<object?> Values => Array.Empty<object?>();

        public object? this[string key] => throw new KeyNotFoundException(key);

        public bool ContainsKey(string key)
        {
            return false;
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            yield break;
        }

        public bool TryGetValue(string key, out object? value)
        {
            value = null;
            return false;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
