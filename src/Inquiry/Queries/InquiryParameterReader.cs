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
            return readOnly;
        }

        if (parameters is IDictionary<string, object?> dictionary)
        {
            return new Dictionary<string, object?>(dictionary, StringComparer.OrdinalIgnoreCase);
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

                result[key] = entry.Value;
            }

            return result;
        }

        return parameters
            .GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0 && property.GetMethod is not null)
            .ToDictionary(property => property.Name, property => property.GetValue(parameters), StringComparer.OrdinalIgnoreCase);
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
