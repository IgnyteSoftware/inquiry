using System.Reflection;
using BenchmarkDotNet.Attributes;

namespace Inquiry.Benchmarks.Contracts;

[AttributeUsage(AttributeTargets.Method)]
public sealed class PrecomputedTransportBenchmarkAttribute : Attribute;

public static class PrecomputedTransportBenchmarkContract
{
    private static readonly string[] ProhibitedTerms = ["Production", "Generated", "Selected"];

    public static void Validate(Type benchmarkType)
    {
        ArgumentNullException.ThrowIfNull(benchmarkType);

        foreach (var method in benchmarkType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            if (method.GetCustomAttribute<PrecomputedTransportBenchmarkAttribute>() is null)
                continue;

            if (method.GetCustomAttribute<BenchmarkAttribute>() is null)
                throw InvalidName(method, "must also be marked as a BenchmarkDotNet benchmark");

            var namesThePrecomputation = method.Name.Contains("Precomputed", StringComparison.OrdinalIgnoreCase)
                || method.Name.Contains("PreSerialized", StringComparison.OrdinalIgnoreCase);
            if (!namesThePrecomputation || !method.Name.Contains("Floor", StringComparison.OrdinalIgnoreCase))
                throw InvalidName(method, "must contain Precomputed or PreSerialized and Floor");

            foreach (var prohibited in ProhibitedTerms)
            {
                if (method.Name.Contains(prohibited, StringComparison.OrdinalIgnoreCase))
                    throw InvalidName(method, $"cannot contain {prohibited}");
            }
        }
    }

    private static InvalidOperationException InvalidName(MethodInfo method, string requirement)
        => new($"Precomputed transport benchmark '{method.DeclaringType?.FullName}.{method.Name}' {requirement}.");
}
