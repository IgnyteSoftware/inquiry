using System.Collections.Immutable;
using Inquiry.Generators.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Inquiry.Generators.Models;

/// <summary>
/// A cacheable, symbol-free description of a diagnostic. Discovery records these into the equatable
/// models; the output stage turns them back into <see cref="Diagnostic"/> instances and reports
/// them. Carrying diagnostics as data (rather than reporting during discovery) is what lets the
/// discovery stages cache.
/// </summary>
internal sealed record DiagnosticData(DiagnosticDescriptor Descriptor, LocationData? Location, EquatableArray<string> MessageArgs)
{
    public static DiagnosticData Create(DiagnosticDescriptor descriptor, Location? location, params string[] messageArgs)
        => new(descriptor, LocationData.From(location), new EquatableArray<string>(messageArgs.ToImmutableArray()));

    public Diagnostic ToDiagnostic()
    {
        var args = MessageArgs.AsImmutableArray();
        var boxed = new object[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            boxed[i] = args[i];
        }

        return Diagnostic.Create(Descriptor, Location?.ToLocation(), boxed);
    }
}

/// <summary>
/// A cacheable stand-in for <see cref="Location"/>. A real <see cref="Location"/> is tied to a
/// specific <c>SyntaxTree</c> instance (which is replaced on every edit), so holding one in a model
/// would defeat caching. The file path + spans are stable while the underlying text is unchanged.
/// </summary>
internal sealed record LocationData(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public static LocationData? From(Location? location)
    {
        if (location?.SourceTree is null)
        {
            return null;
        }

        return new LocationData(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }

    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

    // Equality (the incremental-generator cache key) intentionally EXCLUDES the absolute TextSpan: editing
    // text above an entity on existing lines shifts every character offset below it but not the entity's own
    // line/column, so keying on TextSpan would needlessly re-run the whole emit for a byte-identical entity.
    // FilePath + LineSpan still uniquely place the diagnostic, and TextSpan remains stored for ToLocation.
    // (Inserting/removing a NEWLINE above does shift LineSpan and still invalidates — caching that too would
    // require reporting diagnostics from a pipeline separate from the source-emit output; tracked as a
    // follow-up on #62.)
    public bool Equals(LocationData? other)
        => other is not null && FilePath == other.FilePath && LineSpan.Equals(other.LineSpan);

    public override int GetHashCode()
    {
        unchecked
        {
            return (FilePath?.GetHashCode() ?? 0) * 397 ^ LineSpan.GetHashCode();
        }
    }
}
