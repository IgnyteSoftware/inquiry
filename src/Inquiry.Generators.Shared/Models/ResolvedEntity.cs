using Inquiry.Generators.Infrastructure;

namespace Inquiry.Generators.Models;

/// <summary>
/// One entity after the active dialect resolved its computed-column expressions. The diagnostics are the
/// INQ072 and INQ077 reports for that entity, in reporting order. <see cref="ComputedInvalid"/> is true
/// when a computed expression made the entity unmapped, so stores that depend on it reject their methods.
/// </summary>
internal sealed record ResolvedEntity(
    EntityData Entity,
    EquatableArray<DiagnosticData> Diagnostics,
    bool ComputedInvalid);
