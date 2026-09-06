using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Inquiry.Parameters;

namespace Inquiry.Benchmarks;

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class JsonArrayParameterBenchmarks
{
    private int[] _integers = null!;
    private decimal[] _decimals = null!;
    private string[] _strings = null!;
    private int?[] _nullableIntegers = null!;

    [Params(8, 128, 2048)]
    public int CollectionSize { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _integers = Enumerable.Range(0, CollectionSize).ToArray();
        _decimals = Enumerable.Range(0, CollectionSize).Select(static value => value + 0.25m).ToArray();
        _strings = Enumerable.Range(0, CollectionSize).Select(static value => $"value-{value}").ToArray();
        _nullableIntegers = Enumerable.Range(0, CollectionSize)
            .Select(static value => value % 4 == 0 ? (int?)null : value)
            .ToArray();

        AssertEquivalent(_integers);
        AssertEquivalent(_decimals);
        AssertEquivalent(_strings);
        AssertEquivalent(_nullableIntegers);
    }

    [BenchmarkCategory("Int32"), Benchmark(Baseline = true)]
    public string Int32_Previous() => PreviousToJsonArray(_integers);

    [BenchmarkCategory("Int32"), Benchmark]
    public string Int32_Current() => InquiryJsonArrayParameter.ToJsonArray(_integers);

    [BenchmarkCategory("Decimal"), Benchmark(Baseline = true)]
    public string Decimal_Previous() => PreviousToJsonArray(_decimals);

    [BenchmarkCategory("Decimal"), Benchmark]
    public string Decimal_Current() => InquiryJsonArrayParameter.ToJsonArray(_decimals);

    [BenchmarkCategory("String"), Benchmark(Baseline = true)]
    public string String_Previous() => PreviousToJsonArray(_strings);

    [BenchmarkCategory("String"), Benchmark]
    public string String_Current() => InquiryJsonArrayParameter.ToJsonArray(_strings);

    [BenchmarkCategory("NullableInt32"), Benchmark(Baseline = true)]
    public string NullableInt32_Previous() => PreviousToJsonArray(_nullableIntegers);

    [BenchmarkCategory("NullableInt32"), Benchmark]
    public string NullableInt32_Current() => InquiryJsonArrayParameter.ToJsonArray(_nullableIntegers);

    private static void AssertEquivalent<T>(T[] values)
    {
        var previous = PreviousToJsonArray(values);
        var current = InquiryJsonArrayParameter.ToJsonArray(values);
        if (!string.Equals(previous, current, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"JSON output changed for {typeof(T)}.");
        }
    }

    private static string PreviousToJsonArray<T>(IEnumerable<T> values)
    {
        var sb = new StringBuilder("[");
        var first = true;
        foreach (var value in values)
        {
            if (!first) sb.Append(',');
            first = false;

            if (value is null)
            {
                sb.Append("null");
                continue;
            }

            object boxed = value;
            switch (boxed)
            {
                case string text: AppendJsonString(sb, text); break;
                case int number: sb.Append(number.ToString(CultureInfo.InvariantCulture)); break;
                case decimal number: sb.Append(number.ToString(CultureInfo.InvariantCulture)); break;
                default: throw new NotSupportedException($"The benchmark does not support {typeof(T)}.");
            }
        }

        return sb.Append(']').ToString();
    }

    private static void AppendJsonString(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ' || char.IsSurrogate(c))
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }

        sb.Append('"');
    }
}
