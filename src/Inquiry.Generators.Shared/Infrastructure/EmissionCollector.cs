using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Inquiry.Generators.Infrastructure;

/// <summary>
/// Records the sources and diagnostics of one emitter call, so a pipeline step can cache them as a value
/// and an output node can publish them later, in the same order.
/// </summary>
internal sealed class EmissionCollector
{
    private readonly ImmutableArray<EmittedSource>.Builder _sources = ImmutableArray.CreateBuilder<EmittedSource>();
    private readonly ImmutableArray<EmittedDiagnostic>.Builder _diagnostics = ImmutableArray.CreateBuilder<EmittedDiagnostic>();

    public EquatableArray<EmittedSource> Sources => _sources.ToImmutable();

    public EquatableArray<EmittedDiagnostic> Diagnostics => _diagnostics.ToImmutable();

    public void AddSource(string hintName, SourceText sourceText) => _sources.Add(new EmittedSource(hintName, sourceText));

    public void ReportDiagnostic(Diagnostic diagnostic) => _diagnostics.Add(new EmittedDiagnostic(diagnostic));

    public static void Publish(SourceProductionContext context, EquatableArray<EmittedDiagnostic> diagnostics, EquatableArray<EmittedSource> sources)
    {
        foreach (var diagnostic in diagnostics)
        {
            context.ReportDiagnostic(diagnostic.Diagnostic);
        }

        foreach (var source in sources)
        {
            context.AddSource(source.HintName, source.Text);
        }
    }
}

/// <summary>A generated source recorded by <see cref="EmissionCollector"/>, equal when its hint name and exact text are.</summary>
internal sealed class EmittedSource(string hintName, SourceText text) : IEquatable<EmittedSource>
{
    public string HintName { get; } = hintName;

    public SourceText Text { get; } = text;

    public bool Equals(EmittedSource? other)
        => other is not null && (ReferenceEquals(this, other) ||
            string.Equals(HintName, other.HintName, StringComparison.Ordinal)
            && Equals(Text.Encoding, other.Text.Encoding)
            && Text.ChecksumAlgorithm == other.Text.ChecksumAlgorithm
            && Text.ContentEquals(other.Text));

    public override bool Equals(object? obj) => Equals(obj as EmittedSource);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(HintName) ^ Text.Length;
}

/// <summary>A diagnostic recorded by <see cref="EmissionCollector"/>, published later as the same instance.</summary>
/// <remarks>
/// <see cref="Microsoft.CodeAnalysis.Diagnostic.Equals(object)"/> compares message arguments by reference, so two
/// equal diagnostics with a boxed number argument never compare equal. This type compares what a user sees
/// instead, including the exact text span.
/// </remarks>
internal sealed class EmittedDiagnostic : IEquatable<EmittedDiagnostic>
{
    private readonly string _message;

    public EmittedDiagnostic(Diagnostic diagnostic)
    {
        Diagnostic = diagnostic;
        _message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
    }

    public Diagnostic Diagnostic { get; }

    public bool Equals(EmittedDiagnostic? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        var left = Diagnostic;
        var right = other.Diagnostic;
        return string.Equals(left.Id, right.Id, StringComparison.Ordinal)
            && left.Descriptor.Equals(right.Descriptor)
            && left.Severity == right.Severity
            && left.WarningLevel == right.WarningLevel
            && left.IsSuppressed == right.IsSuppressed
            && string.Equals(_message, other._message, StringComparison.Ordinal)
            && left.Location.Equals(right.Location)
            && left.AdditionalLocations.SequenceEqual(right.AdditionalLocations)
            && PropertiesEqual(left.Properties, right.Properties);
    }

    public override bool Equals(object? obj) => Equals(obj as EmittedDiagnostic);

    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Diagnostic.Id) ^ StringComparer.Ordinal.GetHashCode(_message);

    private static bool PropertiesEqual(ImmutableDictionary<string, string?> left, ImmutableDictionary<string, string?> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value) || !string.Equals(pair.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
